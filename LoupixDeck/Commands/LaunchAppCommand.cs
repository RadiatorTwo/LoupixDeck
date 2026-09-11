using System.Diagnostics;
using LoupixDeck.Commands.Base;
using LoupixDeck.Services.AppLauncher;
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
/// </remarks>
[Command(
    "System.LaunchApp",
    "Launch Application",
    "Shell",
    "({Target})",
    ["Target"],
    [typeof(string)],
    Platform = CommandPlatform.All,
    Hidden = true,
    Description = "Start an installed application")]
public class LaunchAppCommand : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Usage: System.LaunchApp(target)");
            return Task.CompletedTask;
        }

        string target = CommandParameterEncoding.Decode(parameters[0]);
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.WriteLine("System.LaunchApp: empty target");
            return Task.CompletedTask;
        }

        try
        {
            Launch(target);
        }
        catch (Exception ex)
        {
            // An app that was uninstalled or moved is the common case here, so this must never
            // take the button — or the macro chain it sits in — down with it.
            Console.WriteLine($"System.LaunchApp failed for '{target}': {ex.Message}");
        }

        return Task.CompletedTask;
    }

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
            Start(new ProcessStartInfo("xdg-open", target) { UseShellExecute = false });
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