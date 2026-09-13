using System.Diagnostics;

namespace LoupixDeck.Services.Updates;

/// <summary>
/// Starts a process that outlives LoupixDeck. The updater hands its work to an installer that has
/// to keep running after (and while) the app exits.
/// </summary>
public static class DetachedProcess
{
    /// <summary>
    /// Terminal emulators tried in order to run the Linux install script, each with the argument that
    /// separates its own options from the command it runs.
    /// </summary>
    private static readonly (string Name, string[] CommandPrefix)[] Terminals =
    [
        ("x-terminal-emulator", ["-e"]),
        ("gnome-terminal", ["--"]),
        ("konsole", ["-e"]),
        ("xfce4-terminal", ["-x"]),
        ("mate-terminal", ["-x"]),
        ("tilix", ["-e"]),
        ("kitty", []),
        ("alacritty", ["-e"]),
        ("foot", []),
        ("xterm", ["-e"])
    ];

    /// <summary>
    /// Windows: starts <paramref name="path"/> through the shell. The shell, not LoupixDeck, becomes
    /// the parent, so the installer is not torn down when the app exits, and an installer that needs
    /// elevation gets its UAC prompt.
    /// </summary>
    public static void StartViaShell(string path)
    {
        using Process process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        });
    }

    /// <summary>
    /// Linux: runs <paramref name="command"/> in a new terminal window in its own session, so it
    /// survives the app and can ask for the sudo password. False when no terminal is installed.
    /// </summary>
    public static bool TryStartInTerminal(IReadOnlyList<string> command)
    {
        foreach ((string name, string[] prefix) in Terminals)
        {
            string terminal = FindOnPath(name);
            if (terminal is null)
            {
                continue;
            }

            string setsid = FindOnPath("setsid");
            ProcessStartInfo start = new(setsid ?? terminal) { UseShellExecute = false };
            if (setsid is not null)
            {
                start.ArgumentList.Add("-f");
                start.ArgumentList.Add(terminal);
            }

            foreach (string argument in prefix.Concat(command))
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using Process process = Process.Start(start);
                Console.WriteLine($"[Update] Started installer in {name}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] Could not start {name}: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>Opens a web page in the default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            ProcessStartInfo start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo("xdg-open", url) { UseShellExecute = false };

            using Process process = Process.Start(start);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Update] Could not open '{url}': {ex.Message}");
        }
    }

    private static string FindOnPath(string fileName)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
