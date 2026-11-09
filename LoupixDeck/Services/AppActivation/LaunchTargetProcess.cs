using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Works out which process a launch target becomes, for the "Single Instance" check of
/// <c>System.LaunchApp</c>. Empty whenever that cannot be known up front — a launcher URI, a
/// Store app, a game started through Steam — which is why the command also carries an editable
/// process name.
/// </summary>
public static class LaunchTargetProcess
{
    /// <summary>
    /// Programs that start something else, ignored when they only stand between the target and the
    /// application (a desktop entry's Exec, a discovery hint) — a target that is the launcher itself
    /// still means the launcher. Their name says nothing about the application, and
    /// some of them (Steam) are running all the time, so matching on them would be wrong.
    /// </summary>
    private static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "xdg-open", "gtk-launch", "gio", "kioclient", "kioclient5", "kioclient6",
        "sh", "bash", "dash", "zsh", "open", "snap", "lutris", "heroic", "bottles", "wine"
    };

    /// <summary>
    /// The process name for <paramref name="target"/>. <paramref name="executableHint"/> is the
    /// executable discovery found for a target that does not name one itself (a Steam or Epic
    /// game's binary behind its URI).
    /// </summary>
    public static string Guess(string target, string executableHint = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;

        if (target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            return FromDesktopEntry(target);

        // A macOS bundle is single instance through `open` already.
        if (target.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        bool isUri = Uri.TryCreate(target, UriKind.Absolute, out Uri uri) && !uri.IsFile;
        if (isUri || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return FromExecutable(executableHint, skipLaunchers: true);

        return FromExecutable(target, skipLaunchers: false);
    }

    /// <summary>
    /// The window classes <paramref name="target"/> is known to produce, for a backend that can
    /// only match windows by class (GNOME). A desktop entry names them: its
    /// <c>StartupWMClass</c>, its file id (the app_id of a native Wayland window, by convention)
    /// and the application id a Flatpak entry runs. Empty for every other target.
    /// </summary>
    public static IReadOnlyList<string> WindowClasses(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            return [];

        DesktopEntry entry;
        try
        {
            entry = DesktopEntry.Load(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        List<string> classes = [];
        if (!string.IsNullOrWhiteSpace(entry?.StartupWmClass))
            classes.Add(entry.StartupWmClass.Trim());

        classes.Add(Path.GetFileNameWithoutExtension(target));

        if (entry != null)
        {
            string program = FromDesktopEntry(target);
            if (!string.IsNullOrEmpty(program) && program.Contains('.'))
                classes.Add(program);
        }

        return classes.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string FromExecutable(string path, bool skipLaunchers)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            string name = ContextRuleMatcher.Normalize(Path.GetFileName(path.Trim()));
            return skipLaunchers && Launchers.Contains(name) ? string.Empty : name;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The program an entry's <c>Exec</c> runs, looking through <c>env VAR=value</c> prefixes. A
    /// Flatpak entry yields its application id, which matches the window's app_id.
    /// </summary>
    private static string FromDesktopEntry(string path)
    {
        DesktopEntry entry;
        try
        {
            entry = DesktopEntry.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }

        if (entry == null || !entry.TryBuildCommandLine(out string program, out List<string> arguments))
            return string.Empty;

        List<string> tokens = [program, .. arguments];
        int index = 0;
        if (Path.GetFileName(tokens[0]) == "env")
        {
            index = 1;
            while (index < tokens.Count && (tokens[index].Contains('=') || tokens[index].StartsWith('-')))
                index++;
        }

        if (index >= tokens.Count)
            return string.Empty;

        if (Path.GetFileName(tokens[index]) == "flatpak")
        {
            int run = tokens.IndexOf("run", index);
            for (int i = run + 1; run >= 0 && i < tokens.Count; i++)
            {
                if (!tokens[i].StartsWith('-'))
                    return tokens[i];
            }

            return string.Empty;
        }

        return FromExecutable(ResolveProgram(tokens[index]), skipLaunchers: true);
    }

    /// <summary>
    /// The binary <paramref name="program"/> ends up running. Packaged browsers in particular put a
    /// symlink and a shell wrapper in front of it — <c>/usr/bin/thorium-browser</c> links to a script
    /// that runs <c>exec -a "$0" "$HERE/thorium" "$@"</c>, and the process is called
    /// <c>thorium</c>. Falls back to <paramref name="program"/> when nothing better is found.
    /// </summary>
    internal static string ResolveProgram(string program)
    {
        try
        {
            string path = Path.IsPathRooted(program) ? program : SearchPath(program);
            if (path == null || !File.Exists(path))
                return program;

            path = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
            return ExecTargetOfScript(path) ?? path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return program;
        }
    }

    private static string SearchPath(string program)
    {
        string path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// For a shell script, the program its first usable <c>exec</c> line runs, as a file name.
    /// Null when the file is no script or no exec line names a program literally — a target held
    /// only in a variable (<c>exec "$BIN"</c>) cannot be known without running the script.
    /// </summary>
    private static string ExecTargetOfScript(string path)
    {
        FileInfo file = new(path);
        if (file.Length is < 2 or > 64 * 1024)
            return null;

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || !lines[0].StartsWith("#!", StringComparison.Ordinal))
            return null;

        foreach (string line in lines)
        {
            string[] words = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words[0] != "exec")
                continue;

            // Skip exec's own options: -a NAME sets argv[0], -c and -l take no value.
            int i = 1;
            while (i < words.Length && words[i].StartsWith('-'))
                i += words[i] == "-a" ? 2 : 1;
            if (i >= words.Length)
                continue;

            // Wrappers redirect their own streams first (Chromium's: `exec < /dev/null`,
            // `exec > >(exec cat)`); those lines run no program.
            string target = words[i].Trim('"', '\'');
            string name = target[(target.LastIndexOf('/') + 1)..];
            if (IsProgramName(name) && !Launchers.Contains(name))
                return name;
        }

        return null;
    }

    /// <summary>A literal file name: no redirection, variable, quoting or other shell syntax.</summary>
    private static bool IsProgramName(string name)
        => name.Length > 0 && char.IsLetterOrDigit(name[0])
           && name.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '+');
}
