using System.Diagnostics;
using LoupixDeck.Commands.Base;
using LoupixDeck.Services.AppActivation;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.ProcessWatch;
using LoupixDeck.Utils;

namespace LoupixDeck.Commands;

/// <summary>
/// Starts an installed application. The target is whatever identifies it on this platform: an
/// executable path, a launcher URI (<c>steam://</c>, <c>com.epicgames.launcher://</c>), a Microsoft
/// Store activation target (<c>shell:AppsFolder\…</c>) or, on Linux, the path of a <c>.desktop</c>
/// entry.
/// </summary>
/// <remarks>
/// <para>
/// The target is stored escaped by <see cref="CommandParameterEncoding"/>, because a raw path
/// cannot survive <see cref="CommandStringParser"/> — <c>C:\Program Files (x86)\…</c> would be
/// truncated at the <c>)</c>.
/// </para>
/// <para>
/// Hidden: it is inserted by the application picker, which fills and escapes the target. There is
/// nothing useful to type here by hand, and a hand-typed path would have to be escaped manually.
/// </para>
/// <para>
/// "Single Instance" (issue #553) focuses the running application instead of starting it again,
/// and does nothing when it already has focus. "Process Name" says which process that is; empty
/// means "derive it from the target", which works for executables and desktop entries but not
/// for launcher URIs. Both are optional, so a command saved with the target alone keeps
/// launching every time, as before.
/// </para>
/// <para>
/// "Show Running Indicator" (issue #568) has no effect on what a press does: it tells
/// <c>RunningIndicatorManager</c> to draw a dot on the key while the application runs.
/// </para>
/// </remarks>
[Command(
    LaunchAppCommand.CommandName,
    "Launch Application",
    "Shell",
    "({Target}, {Single Instance}, {Process Name}, {Show Running Indicator})",
    ["Target", "Single Instance", "Process Name", "Show Running Indicator"],
    [typeof(string), typeof(bool), typeof(string), typeof(bool)],
    Platform = CommandPlatform.All,
    Hidden = true,
    Description = "Start an installed application")]
public sealed class LaunchAppCommand(IAppWindowActivator activator, IProcessWatcher processWatcher)
    : IExecutableCommand
{
    public const string CommandName = "System.LaunchApp";

    public async Task Execute(string[] parameters)
    {
        if (parameters.Length is < 1 or > 4)
        {
            Console.WriteLine("Usage: System.LaunchApp(target[, singleInstance[, processName[, showRunningIndicator]]])");
            return;
        }

        LaunchAppSpec spec = LaunchAppSpec.FromParameters(parameters);
        string target = spec.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.WriteLine("System.LaunchApp: empty target");
            return;
        }

        string processName = spec.ProcessName;

        try
        {
            if (spec.SingleInstance && await HandledByRunningInstanceAsync(target, processName))
                return;

            Launch(target);

            // An application that starts minimized never takes focus, so the running indicator
            // would not learn about it from a focus change; tell it once instead.
            string watched = spec.ResolveProcessName();
            if (!string.IsNullOrEmpty(watched))
                processWatcher.NotifyLaunched(watched);
        }
        catch (Exception ex)
        {
            // An app that was uninstalled or moved is the common case here, so this must never
            // take the button — or the macro chain it sits in — down with it.
            Console.WriteLine($"System.LaunchApp failed for '{target}': {ex.Message}");
        }
    }

    /// <summary>
    /// True when the application already runs and was focused, or was left alone because it
    /// already has focus or cannot be focused here — anything but a reason to start it.
    /// </summary>
    private async Task<bool> HandledByRunningInstanceAsync(string target, string processName)
    {
        // `open` brings a running bundle forward by itself instead of starting it twice.
        if (OperatingSystem.IsMacOS() && IsMacBundle(target) && string.IsNullOrWhiteSpace(processName))
            return false;

        string name = LaunchAppSpec.ResolveProcessName(target, processName);
        if (string.IsNullOrEmpty(name))
        {
            Console.WriteLine($"System.LaunchApp: no process name for '{target}', launching without the single-instance check.");
            return false;
        }

        AppActivationOutcome outcome = await activator.ActivateAsync(name,
            LaunchTargetProcess.WindowClasses(target), CancellationToken.None);
        switch (outcome)
        {
            case AppActivationOutcome.NotRunning:
            case AppActivationOutcome.NoWindow:
                return false;
            case AppActivationOutcome.Unsupported:
                Console.WriteLine($"System.LaunchApp: '{name}' is running but this session cannot focus its window.");
                return true;
            default:
                return true;
        }
    }

    private static bool IsMacBundle(string target)
        => target.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    private static void Launch(string target)
    {
        if (IsDesktopEntry(target))
        {
            LaunchDesktopEntry(target);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // ShellExecute resolves exe paths, registered URI schemes (steam://, Epic) and
            // "shell:AppsFolder\…" Store targets alike, with no cmd.exe and so no quoting rules.
            Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                WorkingDirectory = WorkingDirectoryFor(target)
            });
            return;
        }

        if (IsUri(target))
        {
            Start(OpenerFor(target));
            return;
        }

        // A macOS .app is a directory, not an executable, so it cannot be started directly —
        // `open` is what resolves the bundle to the binary inside it.
        if (OperatingSystem.IsMacOS() && IsMacBundle(target))
        {
            Start(OpenerFor(target));
            return;
        }

        Start(new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = WorkingDirectoryFor(target)
        });
    }

    private static void LaunchDesktopEntry(string path)
    {
        DesktopEntry entry = DesktopEntry.Load(path);
        if (entry == null)
        {
            Console.WriteLine($"System.LaunchApp: cannot read desktop entry '{path}'");
            return;
        }

        if (!entry.TryBuildCommandLine(out string fileName, out List<string> arguments))
        {
            Console.WriteLine($"System.LaunchApp: desktop entry '{path}' has no usable Exec");
            return;
        }

        ProcessStartInfo startInfo = new(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = !string.IsNullOrWhiteSpace(entry.Path) && Directory.Exists(entry.Path)
                ? entry.Path
                : WorkingDirectoryFor(fileName)
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Terminal=true entries want a terminal emulator wrapped around them. Which one is a
        // per-distribution question with no portable answer, so the program is started directly
        // and simply has no visible console.
        Start(startInfo);
    }

    /// <summary>
    /// The platform opener invoked with <paramref name="target"/> as a single argument.
    /// <see cref="ProcessStartInfo.ArgumentList"/> rather than the Arguments string: the latter is
    /// re-split on whitespace, so any path containing a space — "/Applications/Affinity Photo
    /// 2.app", "~/My Games/..." — arrives at the opener as several nonexistent paths and silently
    /// does nothing.
    /// </summary>
    private static ProcessStartInfo OpenerFor(string target)
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
        {
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(target);
        return startInfo;
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo);
        // The launched application is intentionally left running on its own; we never wait on it.
    }

    private static bool IsDesktopEntry(string target)
        => target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase);

    private static bool IsUri(string target)
        => Uri.TryCreate(target, UriKind.Absolute, out Uri uri) && !uri.IsFile;

    /// <summary>
    /// Working directory for a launched program: its own folder, which is what a desktop shortcut
    /// does and what applications that load files relative to themselves expect. Never our own
    /// directory — a long-lived child would keep a handle on the installation folder and block the
    /// updater from replacing it, the same reason <c>CommandRunner</c> moves shell commands to the
    /// home directory.
    /// </summary>
    private static string WorkingDirectoryFor(string target)
    {
        try
        {
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                return directory;
        }
        catch (ArgumentException)
        {
            // Not a filesystem path (a URI or a shell: target); fall through to the home directory.
        }

        return Environment.GetEnvironmentVariable("HOME")
               ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}