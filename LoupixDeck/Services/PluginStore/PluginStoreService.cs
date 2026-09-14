using System.ComponentModel;
using System.Net.Http;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Updates;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.PluginStore;

public interface IPluginStoreService : INotifyPropertyChanged
{
    /// <summary>Store-managed plugins with a newer compatible release. Changes are raised on the UI thread.</summary>
    IReadOnlyList<PluginStoreItem> AvailableUpdates { get; }

    /// <summary>
    /// The catalog as last loaded, or the on-disk copy of it; null when there has never been one.
    /// Cheap, never touches the network.
    /// </summary>
    PluginCatalog CachedCatalog { get; }

    /// <summary>
    /// Reads the catalog and every listed plugin's releases and matches them against the installed plugins.
    /// Never throws: a catalog that cannot be loaded comes back as an empty list plus an error message.
    /// Release lookups are cached for a few minutes unless <paramref name="force"/> is set.
    /// </summary>
    Task<(IReadOnlyList<PluginStoreItem> Items, string Error)> GetItemsAsync(bool force,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads a release package to a temp file and verifies its SHA-256. Never throws.</summary>
    Task<PluginDownloadResult> DownloadAsync(PluginReleaseCandidate candidate, IProgress<double> progress,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a freshly installed plugin as store-managed (writes <c>store.json</c>).</summary>
    void MarkInstalled(PluginCatalogEntry entry, PluginReleaseCandidate candidate);

    /// <summary>Remembers that a change to <paramref name="pluginId"/> only finishes on the next start.</summary>
    void MarkRestartRequired(string pluginId);

    /// <summary>Checks for plugin updates once in the background, if the automatic update check is on.</summary>
    void StartAutomaticCheck();

    /// <summary>
    /// The plugin a command name belongs to — from the commands plugins provided on this machine, else from
    /// the catalog's command prefixes. Null when the name is not known to belong to any plugin. Never touches
    /// the network.
    /// </summary>
    PluginCommandOwner FindCommandOwner(string commandName);
}

/// <summary>A plugin that owns a command name.</summary>
public sealed record PluginCommandOwner(string PluginId, string DisplayName);

public sealed partial class PluginStoreService : ObservableObject, IPluginStoreService
{
    public const string DefaultCatalogUrl =
        $"https://raw.githubusercontent.com/{GitHubReleaseClient.Repository}/master/plugin-store.json";

    /// <summary><c>ui-settings.json</c> key overriding the catalog location (a URL or a local file) for testing.</summary>
    private const string CatalogUrlKey = "PluginStoreCatalogUrl";

    /// <summary><c>ui-settings.json</c> flag: the plugins present before the store existed were adopted.</summary>
    private const string AdoptedKey = "PluginStoreAdopted";

    private const string CatalogCacheFileName = "plugin-store-cache.json";
    private const string ManifestAssetName = "plugin.json";
    private const string ChecksumAssetName = "SHA256SUMS";

    /// <summary>How many releases per plugin are inspected for a compatible one.</summary>
    private const int MaxReleasesInspected = 10;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The plugin update check waits for the app update check and the first plugin load.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    private readonly IPluginManager _pluginManager;
    private readonly IUpdateService _updateService;
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _restartRequired = new(StringComparer.OrdinalIgnoreCase);

    // Per repository: the resolved releases and when they were read.
    private readonly Dictionary<string, (DateTime ReadAt, ResolvedReleases Releases)> _releaseCache =
        new(StringComparer.OrdinalIgnoreCase);

    private PluginCatalog _cachedCatalog;

    private readonly IPluginCommandIndex _commandIndex;

    public PluginStoreService(IPluginManager pluginManager, IUpdateService updateService, IPluginCommandIndex commandIndex)
    {
        _pluginManager = pluginManager;
        _updateService = updateService;
        _commandIndex = commandIndex;
    }

    public PluginCommandOwner FindCommandOwner(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return null;
        }

        PluginCatalog catalog = CachedCatalog;
        string pluginId = _commandIndex.FindPluginId(commandName);
        PluginCatalogEntry entry = pluginId is null
            ? catalog?.Plugins.FirstOrDefault(e => IsUsableEntry(e) && e.OwnsCommand(commandName))
            : catalog?.Plugins.FirstOrDefault(e => string.Equals(e?.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        pluginId ??= entry?.Id;
        if (pluginId is null)
        {
            return null;
        }

        string name = entry?.DisplayName
                      ?? _pluginManager.Plugins.FirstOrDefault(p =>
                          string.Equals(p.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase))?.Manifest?.Name
                      ?? pluginId;
        return new PluginCommandOwner(pluginId, name);
    }

    [ObservableProperty]
    public partial IReadOnlyList<PluginStoreItem> AvailableUpdates { get; private set; } = [];

    public PluginCatalog CachedCatalog => _cachedCatalog ??= ReadCatalogCache();

    private static string UserPluginsRoot => Path.Combine(FileDialogHelper.GetConfigDir(), "plugins");

    public void StartAutomaticCheck()
    {
        if (!_updateService.AutoCheckEnabled)
        {
            Console.WriteLine("[PluginStore] Automatic update check is turned off.");
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(StartupDelay);
            (IReadOnlyList<PluginStoreItem> items, string error) = await GetItemsAsync(force: false);
            if (error is not null)
            {
                Console.WriteLine($"[PluginStore] Plugin update check failed: {error}");
            }

            foreach (PluginStoreItem item in items.Where(i => i.Status == PluginStoreStatus.UpdateAvailable))
            {
                Console.WriteLine(
                    $"[PluginStore] {item.Entry.DisplayName} {item.Available.Version} is available (installed: {item.InstalledVersion}).");
            }
        });
    }

    public async Task<(IReadOnlyList<PluginStoreItem> Items, string Error)> GetItemsAsync(bool force,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PluginCatalog catalog;
            string catalogError = null;
            try
            {
                catalog = await LoadCatalogAsync(cancellationToken);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                Console.WriteLine($"[PluginStore] Could not load the plugin catalog: {ex.Message}");
                catalog = CachedCatalog;
                catalogError = Loc.Tr("PluginStore_CatalogUnavailable", ex.Message);
                if (catalog is null)
                {
                    return ([], catalogError);
                }
            }

            AdoptExistingPlugins(catalog);

            List<PluginStoreItem> items = [];
            foreach (PluginCatalogEntry entry in catalog.Plugins.Where(IsUsableEntry))
            {
                items.Add(await BuildItemAsync(entry, force, cancellationToken));
            }

            List<PluginStoreItem> updates = items.Where(i => i.Status == PluginStoreStatus.UpdateAvailable).ToList();
            await Dispatcher.UIThread.InvokeAsync(() => AvailableUpdates = updates);

            return (items, catalogError);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginDownloadResult> DownloadAsync(PluginReleaseCandidate candidate, IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "LoupixDeck-plugins", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, candidate.Package.Name);

            await FileDownloader.DownloadAsync(candidate.Package.DownloadUrl, path, progress, cancellationToken);

            // Verify before the archive is ever opened.
            string actual = await FileDownloader.ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(actual, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(directory);
                Console.WriteLine(
                    $"[PluginStore] Checksum mismatch for {candidate.Package.Name} (expected {candidate.Sha256}, got {actual}).");
                return new PluginDownloadResult(null, Loc.Tr("PluginStore_ChecksumMismatch", candidate.Package.Name));
            }

            Console.WriteLine($"[PluginStore] {candidate.Package.Name} verified ({actual}).");
            return new PluginDownloadResult(path);
        }
        catch (OperationCanceledException)
        {
            return new PluginDownloadResult(null, Loc.Tr("PluginStore_DownloadCancelled"));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Console.WriteLine($"[PluginStore] Download of {candidate.Package.Name} failed: {ex.Message}");
            return new PluginDownloadResult(null, Loc.Tr("PluginStore_DownloadFailed", ex.Message));
        }
    }

    public void MarkInstalled(PluginCatalogEntry entry, PluginReleaseCandidate candidate)
    {
        PluginStoreMarker marker = new()
        {
            Repository = entry.Repository,
            Tag = candidate.Release.Tag,
            InstalledAt = DateTime.UtcNow
        };

        // An update of a loaded plugin is staged and swapped in at the next start; the marker has to
        // travel with the staged files, or the old folder's marker would describe the old release.
        string staged = Path.Combine(UserPluginsRoot, PluginInstaller.PendingInstallsDirName, entry.Id);
        string live = Path.Combine(UserPluginsRoot, entry.Id);
        marker.Write(Directory.Exists(staged) ? staged : live);
    }

    public void MarkRestartRequired(string pluginId)
    {
        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            lock (_restartRequired)
            {
                _restartRequired.Add(pluginId);
            }
        }
    }

    /// <summary>
    /// True when a release built against <paramref name="manifest"/>'s SDK loads in this app: same major
    /// version (the loader's rule) and not newer than the SDK this app ships.
    /// </summary>
    public static bool IsCompatible(PluginManifest manifest)
    {
        return manifest is not null
               && Version.TryParse(manifest.SdkVersion, out Version sdk)
               && sdk.Major == SdkInfo.Version.Major
               && sdk <= SdkInfo.Version
               && PluginManager.SupportsCurrentPlatform(manifest);
    }

    /// <summary>
    /// A plugin folder counts as store-managed when it carries a store marker, or when it is a bundled copy
    /// of a catalog plugin (bundled folders are read-only; updates go to the user folder, which wins).
    /// </summary>
    public static bool IsStoreManaged(LoadedPlugin plugin)
    {
        return plugin is not null && (plugin.IsBundled || PluginStoreMarker.Read(plugin.Directory) is not null);
    }

    /// <summary>
    /// Once per installation, the first time a catalog is available: plugins that were bundled with an older
    /// LoupixDeck (and moved into the user plugin folder by its installer) become store-managed, so they get
    /// updates from now on. Plugins installed by hand after that stay manual.
    /// </summary>
    private void AdoptExistingPlugins(PluginCatalog catalog)
    {
        if (UiSettingsStore.GetBool(AdoptedKey, false))
        {
            return;
        }

        foreach (LoadedPlugin plugin in _pluginManager.Plugins.Where(p => !p.IsBundled))
        {
            PluginCatalogEntry entry = catalog.Plugins.FirstOrDefault(e =>
                IsUsableEntry(e) && string.Equals(e.Id, plugin.Manifest?.Id, StringComparison.OrdinalIgnoreCase));
            if (entry is null || PluginStoreMarker.Read(plugin.Directory) is not null)
            {
                continue;
            }

            PluginStoreMarker marker = new() { Repository = entry.Repository, InstalledAt = DateTime.UtcNow };
            if (marker.Write(plugin.Directory))
            {
                Console.WriteLine($"[PluginStore] Adopted {entry.Id} {plugin.Manifest.Version} as store-managed.");
            }
        }

        UiSettingsStore.Set(AdoptedKey, true);
    }

    private static bool IsUsableEntry(PluginCatalogEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry?.Id) && !string.IsNullOrWhiteSpace(entry.Repository);
    }

    private async Task<PluginStoreItem> BuildItemAsync(PluginCatalogEntry entry, bool force,
        CancellationToken cancellationToken)
    {
        LoadedPlugin installed = _pluginManager.Plugins
            .FirstOrDefault(p => string.Equals(p.Manifest?.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        string installedVersion = installed?.Manifest?.Version;

        bool restartRequired;
        lock (_restartRequired)
        {
            restartRequired = _restartRequired.Contains(entry.Id);
        }

        if (restartRequired)
        {
            return new PluginStoreItem(entry, PluginStoreStatus.RestartRequired, installed, installedVersion, null, null);
        }

        if (installed is not null && !IsStoreManaged(installed))
        {
            return new PluginStoreItem(entry, PluginStoreStatus.ManuallyInstalled, installed, installedVersion, null, null);
        }

        ResolvedReleases releases;
        try
        {
            releases = await ResolveReleasesAsync(entry, force, cancellationToken);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Console.WriteLine($"[PluginStore] Could not read the releases of {entry.Repository}: {ex.Message}");
            PluginStoreStatus failedStatus = installed is null ? PluginStoreStatus.Unavailable : PluginStoreStatus.Installed;
            return new PluginStoreItem(entry, failedStatus, installed, installedVersion, null, null,
                Loc.Tr("PluginStore_ReleasesUnavailable", ex.Message));
        }

        PluginStoreStatus status;
        if (installed is null)
        {
            status = releases.Compatible is not null ? PluginStoreStatus.NotInstalled
                : releases.Newest is not null ? PluginStoreStatus.RequiresNewerApp
                : PluginStoreStatus.NoRelease;
        }
        else
        {
            Version current = PluginInstaller.ParseVersion(installedVersion);
            status = releases.Compatible is not null && releases.Compatible.Version > current
                ? PluginStoreStatus.UpdateAvailable
                : PluginStoreStatus.Installed;
        }

        return new PluginStoreItem(entry, status, installed, installedVersion, releases.Compatible, releases.Newest);
    }

    private async Task<ResolvedReleases> ResolveReleasesAsync(PluginCatalogEntry entry, bool force,
        CancellationToken cancellationToken)
    {
        if (!force && _releaseCache.TryGetValue(entry.Repository, out (DateTime ReadAt, ResolvedReleases Releases) cached)
                   && DateTime.UtcNow - cached.ReadAt < CacheLifetime)
        {
            return cached.Releases;
        }

        IReadOnlyList<ReleaseInfo> releases = await _releaseClient.GetStableReleasesAsync(entry.Repository, cancellationToken);

        PluginReleaseCandidate newest = null;
        PluginReleaseCandidate compatible = null;
        foreach (ReleaseInfo release in releases.Take(MaxReleasesInspected))
        {
            PluginReleaseCandidate candidate = await ReadCandidateAsync(entry, release, cancellationToken);
            if (candidate is null)
            {
                continue;
            }

            newest ??= candidate;
            if (IsCompatible(candidate.Manifest))
            {
                compatible = candidate;
                break;
            }
        }

        ResolvedReleases resolved = new(compatible, newest);
        _releaseCache[entry.Repository] = (DateTime.UtcNow, resolved);
        return resolved;
    }

    /// <summary>
    /// The release as an installable candidate, or null when it lacks a package for this platform, a manifest
    /// for the catalog id or a checksum to verify the package against.
    /// </summary>
    private static async Task<PluginReleaseCandidate> ReadCandidateAsync(PluginCatalogEntry entry, ReleaseInfo release,
        CancellationToken cancellationToken)
    {
        string version = $"{release.Version.Major}.{release.Version.Minor}.{release.Version.Build}";
        string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        ReleaseAsset package = release.FindAsset($"{entry.Id}-{version}-{platform}.zip")
                               ?? release.FindAsset($"{entry.Id}-{version}-any.zip");
        ReleaseAsset manifestAsset = release.FindAsset(ManifestAssetName);
        if (package is null || manifestAsset is null)
        {
            return null;
        }

        PluginManifest manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<PluginManifest>(
                await FileDownloader.DownloadStringAsync(manifestAsset.DownloadUrl, cancellationToken));
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[PluginStore] {entry.Repository} {release.Tag}: invalid plugin.json ({ex.Message}).");
            return null;
        }

        if (!string.Equals(manifest?.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[PluginStore] {entry.Repository} {release.Tag}: plugin.json id does not match '{entry.Id}'.");
            return null;
        }

        string sha256 = null;
        ReleaseAsset checksums = release.FindAsset(ChecksumAssetName);
        if (checksums is not null)
        {
            string content = await FileDownloader.DownloadStringAsync(checksums.DownloadUrl, cancellationToken);
            ParseChecksums(content).TryGetValue(package.Name, out sha256);
        }

        sha256 ??= package.Sha256;
        if (sha256 is null)
        {
            Console.WriteLine($"[PluginStore] {entry.Repository} {release.Tag}: no checksum for {package.Name}, skipped.");
            return null;
        }

        return new PluginReleaseCandidate(release, manifest, package, sha256);
    }

    /// <summary>Parses <c>sha256sum</c> output: <c>&lt;hex&gt;  &lt;file&gt;</c> or <c>&lt;hex&gt; *&lt;file&gt;</c> per line.</summary>
    internal static Dictionary<string, string> ParseChecksums(string content)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in (content ?? string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            int space = line.IndexOf(' ');
            if (space != 64)
            {
                continue;
            }

            string hash = line[..space];
            string file = line[space..].Trim().TrimStart('*');
            if (file.Length > 0 && hash.All(Uri.IsHexDigit))
            {
                result[file] = hash;
            }
        }

        return result;
    }

    private async Task<PluginCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        string location = UiSettingsStore.GetString(CatalogUrlKey);
        if (string.IsNullOrWhiteSpace(location))
        {
            location = DefaultCatalogUrl;
        }

        string json = File.Exists(location)
            ? await File.ReadAllTextAsync(location, cancellationToken)
            : await FileDownloader.DownloadStringAsync(location, cancellationToken);

        PluginCatalog catalog = JsonConvert.DeserializeObject<PluginCatalog>(json)
                                ?? throw new InvalidOperationException("The plugin catalog is empty.");
        catalog.Plugins ??= [];

        _cachedCatalog = catalog;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(FileDialogHelper.GetConfigDir(), CatalogCacheFileName), json,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[PluginStore] Could not cache the plugin catalog: {ex.Message}");
        }

        return catalog;
    }

    private static PluginCatalog ReadCatalogCache()
    {
        string path = Path.Combine(FileDialogHelper.GetConfigDir(), CatalogCacheFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            PluginCatalog catalog = JsonConvert.DeserializeObject<PluginCatalog>(File.ReadAllText(path));
            if (catalog is not null)
            {
                catalog.Plugins ??= [];
            }

            return catalog;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[PluginStore] Could not read the cached plugin catalog: {ex.Message}");
            return null;
        }
    }

    private static bool IsExpectedFailure(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best-effort.
        }
    }

    private sealed record ResolvedReleases(PluginReleaseCandidate Compatible, PluginReleaseCandidate Newest);
}
