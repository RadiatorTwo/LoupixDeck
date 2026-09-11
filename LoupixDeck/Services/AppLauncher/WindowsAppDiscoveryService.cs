#if WINDOWS
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Finds applications from the Start Menu and from the Steam and Epic libraries.
/// </summary>
/// <remarks>
/// Microsoft Store apps are not enumerated. They live only in the virtual <c>shell:AppsFolder</c>,
/// which has no on-disk listing, and the ways to read it (a PowerShell <c>Get-AppxPackage</c> hop or
/// late-bound Shell COM) each cost more at start-up than the coverage is worth here.
/// <c>System.LaunchApp</c> already launches a <c>shell:AppsFolder\…</c> target, so support can be
/// added later without changing anything the user has configured.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsAppDiscoveryService : AppDiscoveryServiceBase
{
    /// <summary>
    /// Executables a shortcut may resolve to that are maintenance rather than an application:
    /// uninstallers and installer hosts.
    /// </summary>
    /// <remarks>
    /// Matched against the resolved <b>target</b>, never the shortcut's display name. A name list
    /// only works in the language the list was written in — an English "uninstall" list happily lets
    /// "Advanced IP Scanner deinstallieren" through on a German system, and the fork this feature
    /// came from had the same bug in Spanish. The executable name is the same everywhere.
    /// Shortcuts to documentation are filtered for free, since only a <c>.exe</c> target is kept at
    /// all and readme/website entries point at a document or a <c>.url</c>.
    /// </remarks>
    private static readonly string[] IgnoredTargetNames =
    [
        "msiexec", "unins", "uninst", "uninstall", "setup", "install"
    ];

    /// <summary>
    /// Steam entries that are runtimes and tooling rather than games. Name matching is safe here,
    /// unlike for shortcuts: these are Valve's own product names, which are not translated.
    /// </summary>
    private static readonly string[] IgnoredSteamNames =
    [
        "redistributable", "proton", "steam linux runtime", "steamworks", "dedicated server"
    ];

    /// <summary>Executables that ship next to a game but are not the game.</summary>
    private static readonly string[] IgnoredExecutableNames =
    [
        "unins", "crash", "report", "redist", "setup", "install", "launcher", "unitycrash", "vcredist"
    ];

    public override bool IsSupported => true;

    protected override IEnumerable<InstalledApp> Discover(CancellationToken cancellationToken)
    {
        // One resolver for the whole scan: each instance is a COM activation.
        using WindowsShortcutResolver shortcuts = new();

        foreach (InstalledApp app in DiscoverStartMenu(shortcuts, cancellationToken))
            yield return app;

        foreach (InstalledApp app in DiscoverSteam(cancellationToken))
            yield return app;

        foreach (InstalledApp app in DiscoverEpic(cancellationToken))
            yield return app;
    }

    // ───────────────────────── Start Menu ─────────────────────────

    private static IEnumerable<InstalledApp> DiscoverStartMenu(
        WindowsShortcutResolver shortcuts, CancellationToken cancellationToken)
    {
        foreach (string root in StartMenuRoots())
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[AppDiscovery] Cannot list '{root}': {ex.Message}");
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only the executable is kept, not the shortcut's arguments. Application shortcuts
                // rarely carry any; the utility entries that do (a control-panel applet, a driver
                // helper) are not what someone binds to a deck button.
                string target = shortcuts.Resolve(file);
                if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsIgnored(Path.GetFileNameWithoutExtension(target), IgnoredTargetNames))
                    continue;

                yield return new InstalledApp
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Target = target,
                    Source = AppSource.Installed,
                    IconExtractSource = target
                };
            }
        }
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        foreach (Environment.SpecialFolder folder in
                 new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path))
            {
                string programs = Path.Combine(path, "Programs");
                if (Directory.Exists(programs))
                    yield return programs;
            }
        }
    }

    // ───────────────────────── Steam ─────────────────────────

    private static IEnumerable<InstalledApp> DiscoverSteam(CancellationToken cancellationToken)
    {
        string steamRoot = SteamRoot();
        if (steamRoot == null)
            yield break;

        foreach (string library in SteamLibraries(steamRoot))
        {
            string steamApps = Path.Combine(library, "steamapps");

            string[] manifests;
            try
            {
                manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[AppDiscovery] Cannot list Steam library '{steamApps}': {ex.Message}");
                continue;
            }

            foreach (string manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                InstalledApp app = ReadSteamManifest(manifest, steamApps, steamRoot);
                if (app != null)
                    yield return app;
            }
        }
    }

    private static InstalledApp ReadSteamManifest(string manifest, string steamApps, string steamRoot)
    {
        string text;
        try
        {
            text = File.ReadAllText(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot read '{manifest}': {ex.Message}");
            return null;
        }

        string appId = AcfValue(text, "appid");
        string name = AcfValue(text, "name");
        string installDir = AcfValue(text, "installdir");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
            return null;

        if (IsIgnored(name, IgnoredSteamNames))
            return null;

        string gameDirectory = string.IsNullOrWhiteSpace(installDir)
            ? null
            : Path.Combine(steamApps, "common", installDir);

        return new InstalledApp
        {
            Name = name,
            // Launch through Steam rather than the executable: a game started directly often
            // refuses to run, and Steam handles its own updates and overlay this way.
            Target = $"steam://rungameid/{appId}",
            Source = AppSource.Steam,
            IsGame = true,
            PreResolvedIcon = SteamArtwork(steamRoot, appId),
            IconExtractSource = FindGameExecutable(gameDirectory)
        };
    }

    /// <summary>Steam's install root, from the registry first so a non-default install is found.</summary>
    private static string SteamRoot()
    {
        try
        {
            // Fully qualified: the project's own LoupixDeck.Registry namespace shadows an
            // unqualified "Registry" inside LoupixDeck.*.
            using Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string registered)
            {
                string path = registered.Replace('/', '\\');
                if (Directory.Exists(path))
                    return path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot read the Steam registry key: {ex.Message}");
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Every Steam library folder, including those on other drives, from
    /// <c>steamapps\libraryfolders.vdf</c>. The install root is always included.
    /// </summary>
    private static IEnumerable<string> SteamLibraries(string steamRoot)
    {
        HashSet<string> libraries = new(StringComparer.OrdinalIgnoreCase) { steamRoot };

        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
            {
                foreach (Match match in VdfPathRegex().Matches(File.ReadAllText(vdf)))
                {
                    string path = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(path))
                        libraries.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot read '{vdf}': {ex.Message}");
        }

        return libraries;
    }

    private static string SteamArtwork(string steamRoot, string appId)
    {
        string cache = Path.Combine(steamRoot, "appcache", "librarycache", appId);
        foreach (string candidate in new[] { "logo.png", "library_600x900.jpg", "header.jpg" })
        {
            string path = Path.Combine(cache, candidate);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    // ───────────────────────── Epic ─────────────────────────

    private static IEnumerable<InstalledApp> DiscoverEpic(CancellationToken cancellationToken)
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string manifestDirectory = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");

        string[] manifests;
        try
        {
            if (!Directory.Exists(manifestDirectory))
                yield break;

            manifests = Directory.GetFiles(manifestDirectory, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot list '{manifestDirectory}': {ex.Message}");
            yield break;
        }

        foreach (string manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InstalledApp app = ReadEpicManifest(manifest);
            if (app != null)
                yield return app;
        }
    }

    private static InstalledApp ReadEpicManifest(string manifest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("bIsIncompleteInstall", out JsonElement incomplete)
                && incomplete.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            if (!root.TryGetProperty("DisplayName", out JsonElement displayName)
                || !root.TryGetProperty("AppName", out JsonElement appName))
            {
                return null;
            }

            string name = displayName.GetString();
            string id = appName.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                return null;

            bool isGame = root.TryGetProperty("AppCategories", out JsonElement categories)
                          && categories.ValueKind == JsonValueKind.Array
                          && categories.EnumerateArray().Any(c =>
                              string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase));

            return new InstalledApp
            {
                Name = name,
                Target = $"com.epicgames.launcher://apps/{id}?action=launch&silent=true",
                Source = AppSource.Epic,
                IsGame = isGame,
                IconExtractSource = EpicExecutable(root)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot read '{manifest}': {ex.Message}");
            return null;
        }
    }

    private static string EpicExecutable(JsonElement root)
    {
        if (!root.TryGetProperty("InstallLocation", out JsonElement location)
            || !root.TryGetProperty("LaunchExecutable", out JsonElement executable))
        {
            return null;
        }

        string directory = location.GetString();
        string relative = executable.GetString();
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(relative))
            return null;

        try
        {
            string path = Path.Combine(directory, relative.Replace('/', '\\'));
            return File.Exists(path) ? path : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ───────────────────────── Shared helpers ─────────────────────────

    /// <summary>
    /// Picks the executable most likely to be the game itself: the largest one, searching only the
    /// top two directory levels. A game tree can hold thousands of files, so an unbounded recursive
    /// walk is the single most expensive thing a cold scan could do.
    /// </summary>
    private static string FindGameExecutable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        try
        {
            List<string> candidates = [];
            candidates.AddRange(Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly));

            foreach (string child in Directory.EnumerateDirectories(directory))
                candidates.AddRange(Directory.EnumerateFiles(child, "*.exe", SearchOption.TopDirectoryOnly));

            return candidates
                .Where(path => !IsIgnored(Path.GetFileNameWithoutExtension(path), IgnoredExecutableNames))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.Length)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppDiscovery] Cannot scan '{directory}': {ex.Message}");
            return null;
        }
    }

    private static bool IsIgnored(string name, string[] needles)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        foreach (string needle in needles)
        {
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Reads one <c>"key" "value"</c> pair out of Valve's KeyValues text format. The
    /// manifests are flat enough that a full parser would not buy anything.</summary>
    private static string AcfValue(string text, string key)
    {
        foreach (Match match in AcfPairRegex().Matches(text))
        {
            if (string.Equals(match.Groups[1].Value, key, StringComparison.OrdinalIgnoreCase))
                return match.Groups[2].Value;
        }

        return null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();

    [GeneratedRegex("\"([^\"]+)\"\\s+\"([^\"]*)\"")]
    private static partial Regex AcfPairRegex();
}
#endif