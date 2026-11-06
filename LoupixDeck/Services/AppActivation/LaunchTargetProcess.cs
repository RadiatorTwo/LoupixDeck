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

        return FromExecutable(tokens[index], skipLaunchers: true);
    }
}
