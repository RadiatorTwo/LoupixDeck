using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Focuses a window on KDE Plasma (Wayland), where KWin exposes no window list over IPC but
/// runs JavaScript on request: a small script is loaded through <c>org.kde.KWin /Scripting</c>
/// and finds and activates the window inside KWin itself.
/// </summary>
/// <remarks>
/// <para>
/// The script has no channel back, so it decides on its own: a matching window that is already
/// active is left alone, otherwise the first match is activated. Whether the application runs at
/// all is decided by the caller from /proc before this is called.
/// </para>
/// <para>
/// KWin loads a script file asynchronously after <c>run</c>, so a script is unloaded before the
/// next one is loaded rather than right after it ran. <c>dbus-send</c> is used because it ships
/// with D-Bus itself, unlike qdbus whose name differs between Plasma 5 and 6.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class KWinWindowActivator
{
    private const string PluginName = "loupixdeck-activate";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>True on a Plasma Wayland session, where this is the way to focus a window.</summary>
    public static bool IsKdeWayland()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
           && (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
           .Contains("KDE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Activates the first window owned by one of <paramref name="processIds"/> or whose window
    /// class or desktop file name equals <paramref name="name"/>, unless one already is active.
    /// </summary>
    public async Task ActivateAsync(string name, IReadOnlyCollection<int> processIds, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            string path = ScriptPath();
            await DbusSendAsync(cancellationToken, "/Scripting", "org.kde.kwin.Scripting.unloadScript",
                $"string:{PluginName}");
            await File.WriteAllTextAsync(path, BuildScript(name, processIds), cancellationToken);

            string loaded = await DbusSendAsync(cancellationToken, "/Scripting", "org.kde.kwin.Scripting.loadScript",
                $"string:{path}", $"string:{PluginName}");
            if (!TryParseScriptId(loaded, out int id) || id < 0)
                throw new InvalidOperationException($"KWin did not load the script: {loaded.Trim()}");

            // Plasma 6 registers the script under /Scripting/Script<id>; Plasma 5 used /<id>.
            try
            {
                await DbusSendAsync(cancellationToken, $"/Scripting/Script{id}", "org.kde.kwin.Script.run");
            }
            catch (InvalidOperationException)
            {
                await DbusSendAsync(cancellationToken, $"/{id}", "org.kde.kwin.Script.run");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string ScriptPath()
    {
        string directory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            directory = Path.GetTempPath();
        return Path.Combine(directory, "loupixdeck-kwin-activate.js");
    }

    /// <summary>
    /// The script. Values go in as JSON literals, so no window class or name can break out of
    /// the string. Plasma 6 names are tried first, Plasma 5's (<c>clientList</c>,
    /// <c>activeClient</c>) are the fallback.
    /// </summary>
    internal static string BuildScript(string name, IReadOnlyCollection<int> processIds)
    {
        StringBuilder script = new();
        script.Append("const pids = ").Append(JsonSerializer.Serialize(processIds)).Append(";\n");
        script.Append("const name = ").Append(JsonSerializer.Serialize(name.ToLowerInvariant())).Append(";\n");
        script.Append("""
            const plasma6 = typeof workspace.windowList === "function";
            const windows = plasma6 ? workspace.windowList() : workspace.clientList();
            const active = plasma6 ? workspace.activeWindow : workspace.activeClient;
            const lower = value => (value || "").toString().toLowerCase();
            const matches = w => !!w && w.normalWindow !== false && (
                pids.indexOf(w.pid) >= 0 ||
                lower(w.resourceClass) === name ||
                lower(w.resourceName) === name ||
                lower(w.desktopFileName) === name);
            if (!matches(active)) {
                for (let i = 0; i < windows.length; i++) {
                    const w = windows[i];
                    if (!matches(w)) continue;
                    if (w.minimized) w.minimized = false;
                    if (plasma6) workspace.activeWindow = w; else workspace.activeClient = w;
                    break;
                }
            }
            """);
        return script.ToString();
    }

    /// <summary>Reads the int32 from dbus-send's "method return … int32 N" reply.</summary>
    private static bool TryParseScriptId(string reply, out int id)
    {
        id = -1;
        string[] tokens = reply.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int index = Array.LastIndexOf(tokens, "int32");
        return index >= 0 && index + 1 < tokens.Length
               && int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static async Task<string> DbusSendAsync(CancellationToken cancellationToken, string objectPath,
        string method, params string[] arguments)
    {
        ProcessStartInfo start = new("dbus-send")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--session");
        start.ArgumentList.Add("--print-reply");
        start.ArgumentList.Add("--dest=org.kde.KWin");
        start.ArgumentList.Add(objectPath);
        start.ArgumentList.Add(method);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)
                                ?? throw new InvalidOperationException("dbus-send could not be started");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallTimeout);

        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch (InvalidOperationException) { /* already exited */ }
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dbus-send {method} failed: {(await error).Trim()}");

        return await output;
    }
}
