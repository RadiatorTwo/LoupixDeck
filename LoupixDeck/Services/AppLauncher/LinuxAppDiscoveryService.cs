using System.Runtime.Versioning;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Finds applications from XDG desktop entries — the same source the desktop's own application menu
/// uses, so the picker lists what the user already recognises. Covers native packages, Flatpak and
/// Snap, because all three export a <c>.desktop</c> file into a standard data directory.
/// </summary>
/// <remarks>
/// Needs no <c>#if</c> guard: it is plain file IO and compiles everywhere, matching how
/// <c>LinuxActiveWindowMonitor</c> is written.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxAppDiscoveryService : AppDiscoveryServiceBase
{
    public override bool IsSupported => true;

    protected override IEnumerable<InstalledApp> Discover(CancellationToken cancellationToken)
    {
        foreach (string directory in ApplicationDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.desktop", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[AppDiscovery] Cannot list '{directory}': {ex.Message}");
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                InstalledApp app = Read(file);
                if (app != null)
                    yield return app;
            }
        }
    }

    private static InstalledApp Read(string file)
    {
        DesktopEntry entry;
        try
        {
            entry = DesktopEntry.Load(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot read '{file}': {ex.Message}");
            return null;
        }

        if (entry == null || !entry.IsVisibleApplication)
            return null;

        // A desktop-specific entry (GNOME's or KDE's own control panels) is menu furniture, not
        // something to put on a deck button.
        if (entry.OnlyShowIn.Count > 0)
            return null;

        // TryExec names the binary that decides whether the entry is actually installed. A relative
        // name is looked up on PATH; anything not found means the entry is stale.
        if (!string.IsNullOrWhiteSpace(entry.TryExec) && ResolveExecutable(entry.TryExec) == null)
            return null;

        string executable = entry.TryBuildCommandLine(out string fileName, out _)
            ? ResolveExecutable(fileName)
            : null;

        return new InstalledApp
        {
            Name = entry.Name,
            // The .desktop path is the launch target: it carries the arguments, the working
            // directory and the terminal flag that the bare binary would lose.
            Target = entry.FilePath,
            Source = AppSource.Installed,
            IsGame = entry.Categories.Contains("Game", StringComparer.Ordinal),
            PreResolvedIcon = AbsoluteIconPath(entry.Icon),
            IconExtractSource = executable
        };
    }

    /// <summary>
    /// The directories desktop entries live in, in XDG precedence order: the user's own first, then
    /// the system ones. Flatpak and Snap export into these same paths, so they need no special case
    /// beyond making sure the defaults are present when the variables are unset.
    /// </summary>
    private static IEnumerable<string> ApplicationDirectories()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string root in DataDirectories())
        {
            string directory = Path.Combine(root, "applications");
            if (seen.Add(directory) && Directory.Exists(directory))
                yield return directory;
        }
    }

    private static IEnumerable<string> DataDirectories()
    {
        string home = Environment.GetEnvironmentVariable("HOME")
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        yield return !string.IsNullOrWhiteSpace(dataHome)
            ? dataHome
            : Path.Combine(home, ".local", "share");

        // Per-user Flatpak exports are not always covered by XDG_DATA_DIRS.
        yield return Path.Combine(home, ".local", "share", "flatpak", "exports", "share");

        string dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (!string.IsNullOrWhiteSpace(dataDirs))
        {
            foreach (string dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return dir;
        }
        else
        {
            yield return "/usr/local/share";
            yield return "/usr/share";
        }

        // Defaults that a trimmed XDG_DATA_DIRS can leave out.
        yield return "/var/lib/flatpak/exports/share";
        yield return "/var/lib/snapd/desktop";
    }

    /// <summary>Resolves a program name to a full path, searching PATH for a bare name. Returns null
    /// when it is not installed.</summary>
    private static string ResolveExecutable(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
            return null;

        try
        {
            if (program.Contains('/'))
                return File.Exists(program) ? program : null;

            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return null;

            foreach (string directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, program);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot resolve '{program}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// A desktop entry's <c>Icon</c> is either an absolute path or a theme name. Only the absolute
    /// form is usable without a theme lookup, which the icon extractor does.
    /// </summary>
    private static string AbsoluteIconPath(string icon)
        => !string.IsNullOrWhiteSpace(icon) && Path.IsPathRooted(icon) && File.Exists(icon) ? icon : null;
}