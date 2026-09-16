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
    /// Reads the catalog and matches the versions it publishes against the installed plugins. Exactly one
    /// request, whatever the number of plugins: the catalog is the source of the versions, so no plugin
    /// repository is ever contacted. Never throws — a catalog that cannot be loaded falls back to the copy
    /// on disk, or comes back as an empty list plus an error message.
    /// </summary>
    Task<PluginStoreResult> GetItemsAsync(bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// The release notes of the version about to be installed — one request, made only when the user asked
    /// for an install or update. Never throws; null when they cannot be read.
    /// </summary>
    Task<string> GetReleaseNotesAsync(PluginReleaseCandidate candidate,
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

    /// <summary>
    /// Plugins the device configs or macros use commands of, but which are not installed. Reads the files on
    /// disk; loads the catalog once when there is no cached copy yet. Never throws.
    /// </summary>
    Task<IReadOnlyList<PluginCommandOwner>> FindMissingPluginsAsync(CancellationToken cancellationToken = default);
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

    /// <summary>The plugin update check waits for the app update check and the first plugin load.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>How long a downloaded list is reused when the page is reopened; Refresh ignores it.</summary>
    private static readonly TimeSpan CatalogFreshness = TimeSpan.FromMinutes(5);

    private readonly IPluginManager _pluginManager;
    private readonly IUpdateService _updateService;

    /// <summary>Only for the release notes of a version the user is installing; never for the list itself.</summary>
    private readonly GitHubReleaseClient _releaseClient = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _restartRequired = new(StringComparer.OrdinalIgnoreCase);

    private PluginCatalog _cachedCatalog;

    /// <summary>When the copy on disk was written; null while no copy has been read or written.</summary>
    private DateTimeOffset? _cacheWrittenAt;

    /// <summary>When the list was last downloaded; null while it only ever came from disk.</summary>
    private DateTimeOffset? _catalogLoadedAt;

    private readonly IPluginCommandIndex _commandIndex;

    public PluginStoreService(IPluginManager pluginManager, IUpdateService updateService, IPluginCommandIndex commandIndex)
    {
        _pluginManager = pluginManager;
        _updateService = updateService;
        _commandIndex = commandIndex;
    }

    public async Task<IReadOnlyList<PluginCommandOwner>> FindMissingPluginsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (CachedCatalog is null)
            {
                await LoadCatalogAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            // Without a catalog only commands this machine has seen before are recognised.
            Console.WriteLine($"[PluginStore] Could not load the plugin catalog: {ex.Message}");
        }

        try
        {
            HashSet<string> names = await Task.Run(CollectConfiguredCommandNames, cancellationToken);
            HashSet<string> installed = new(
                _pluginManager.Plugins.Select(p => p.Manifest?.Id).Where(id => id is not null),
                StringComparer.OrdinalIgnoreCase);

            return names
                .Select(FindCommandOwner)
                .Where(owner => owner is not null && !installed.Contains(owner.PluginId))
                .DistinctBy(owner => owner.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Console.WriteLine($"[PluginStore] Could not check the configs for missing plugins: {ex.Message}");
            return [];
        }
    }

    /// <summary>Command names used by every device config (<c>config_*.json</c>) and by the macros.</summary>
    private static HashSet<string> CollectConfiguredCommandNames()
    {
        string configDir = FileDialogHelper.GetConfigDir();
        IEnumerable<string> files = Directory.GetFiles(configDir, "config*.json")
            .Append(Path.Combine(configDir, "macros.json"))
            .Where(File.Exists);

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            try
            {
                names.UnionWith(Portable.PortableCommandScanner.CollectCommandNames(
                    Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(file))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.WriteLine($"[PluginStore] Could not scan '{file}' for plugin commands: {ex.Message}");
            }
        }

        return names;
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
            PluginStoreResult result = await GetItemsAsync(force: false);
            if (result.Error is not null)
            {
                Console.WriteLine($"[PluginStore] Plugin update check failed: {result.Error}");
            }

            foreach (PluginStoreItem item in result.Items.Where(i => i.Status == PluginStoreStatus.UpdateAvailable))
            {
                Console.WriteLine(
                    $"[PluginStore] {item.Entry.DisplayName} {item.Available.Version} is available (installed: {item.InstalledVersion}).");
            }
        });
    }

    public async Task<PluginStoreResult> GetItemsAsync(bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PluginCatalog catalog;
            string catalogError = null;
            bool fromCache = false;
            try
            {
                // Reopening the page reuses the list that was just downloaded; Refresh always reloads.
                catalog = !force && _cachedCatalog is not null && _catalogLoadedAt is { } loadedAt
                                 && DateTimeOffset.UtcNow - loadedAt < CatalogFreshness
                    ? _cachedCatalog
                    : await LoadCatalogAsync(cancellationToken);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                Console.WriteLine($"[PluginStore] Could not load the plugin catalog: {ex.Message}");
                catalog = CachedCatalog;
                catalogError = Loc.Tr("PluginStore_CatalogUnavailable", ex.Message);
                if (catalog is null)
                {
                    return new PluginStoreResult([], catalogError);
                }

                fromCache = true;
            }

            AdoptExistingPlugins(catalog);

            // Purely local: the catalog already carries every version, so the number of plugins is irrelevant.
            List<PluginStoreItem> items = catalog.Plugins.Where(IsUsableEntry).Select(BuildItem).ToList();

            List<PluginStoreItem> updates = items.Where(i => i.Status == PluginStoreStatus.UpdateAvailable).ToList();
            await Dispatcher.UIThread.InvokeAsync(() => AvailableUpdates = updates);

            return new PluginStoreResult(items, catalogError, fromCache, fromCache ? _cacheWrittenAt : null,
                catalog.IsOutdatedSchema);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetReleaseNotesAsync(PluginReleaseCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Entry.Repository))
        {
            return null;
        }

        try
        {
            ReleaseInfo release =
                await _releaseClient.GetReleaseByTagAsync(candidate.Entry.Repository, candidate.Tag,
                    cancellationToken);
            return string.IsNullOrWhiteSpace(release?.Notes) ? null : release.Notes.Trim();
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            // Notes are a courtesy; never let them get in the way of installing.
            // GitHubRateLimitException is an HttpRequestException, so it lands here too.
            Console.WriteLine(
                $"[PluginStore] Could not read the release notes of {candidate.Entry.Repository} {candidate.Tag}: {ex.Message}");
            return null;
        }
    }

    public async Task<PluginDownloadResult> DownloadAsync(PluginReleaseCandidate candidate, IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "LoupixDeck-plugins", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, candidate.FileName);

            await FileDownloader.DownloadAsync(candidate.DownloadUrl, path, progress, cancellationToken);

            // Verify before the archive is ever opened.
            string actual = await FileDownloader.ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(actual, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(directory);
                Console.WriteLine(
                    $"[PluginStore] Checksum mismatch for {candidate.FileName} (expected {candidate.Sha256}, got {actual}).");
                return new PluginDownloadResult(null, Loc.Tr("PluginStore_ChecksumMismatch", candidate.FileName));
            }

            Console.WriteLine($"[PluginStore] {candidate.FileName} verified ({actual}).");
            return new PluginDownloadResult(path);
        }
        catch (OperationCanceledException)
        {
            return new PluginDownloadResult(null, Loc.Tr("PluginStore_DownloadCancelled"));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            Console.WriteLine($"[PluginStore] Download of {candidate.FileName} failed: {ex.Message}");
            return new PluginDownloadResult(null, Loc.Tr("PluginStore_DownloadFailed", ex.Message));
        }
    }

    public void MarkInstalled(PluginCatalogEntry entry, PluginReleaseCandidate candidate)
    {
        PluginStoreMarker marker = new()
        {
            Repository = entry.Repository,
            Tag = candidate.Tag,
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
    /// True when a release built against <paramref name="sdkVersion"/> loads in this app: same major version
    /// (the loader's rule) and not newer than the SDK this app ships. The platform is not checked here — it is
    /// already decided by which package the release ships for this system.
    /// </summary>
    public static bool IsCompatible(string sdkVersion)
    {
        return Version.TryParse(sdkVersion, out Version sdk)
               && sdk.Major == SdkInfo.Version.Major
               && sdk <= SdkInfo.Version;
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

    private PluginStoreItem BuildItem(PluginCatalogEntry entry)
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

        PluginReleaseCandidate candidate = ReadCandidate(entry);
        if (candidate is null)
        {
            // An entry without release information comes from a list older than this app understands; one
            // that has a release but no package simply does not ship a build for this system.
            bool noReleaseInfo = entry.Release is null
                                 || PluginInstaller.ParseVersion(entry.Release.Version) <= new Version(0, 0);
            PluginStoreStatus missingStatus = installed is not null
                ? PluginStoreStatus.Installed
                : noReleaseInfo
                    ? PluginStoreStatus.Unavailable
                    : PluginStoreStatus.NoRelease;

            return new PluginStoreItem(entry, missingStatus, installed, installedVersion, null, null,
                noReleaseInfo && installed is null ? Loc.Tr("PluginStore_NoReleaseInfo") : null);
        }

        PluginReleaseCandidate compatible = IsCompatible(candidate.Release.SdkVersion) ? candidate : null;

        PluginStoreStatus status;
        if (installed is null)
        {
            status = compatible is not null ? PluginStoreStatus.NotInstalled : PluginStoreStatus.RequiresNewerApp;
        }
        else
        {
            Version current = PluginInstaller.ParseVersion(installedVersion);
            status = compatible is not null && compatible.Version > current
                ? PluginStoreStatus.UpdateAvailable
                : PluginStoreStatus.Installed;
        }

        // Newest is the release either way, so the page can say that a newer one needs a newer app.
        return new PluginStoreItem(entry, status, installed, installedVersion, compatible, candidate);
    }

    /// <summary>
    /// The entry's release as an installable candidate, or null when the list carries no release for it or
    /// the release ships no usable package for this system. Never touches the network.
    /// </summary>
    private static PluginReleaseCandidate ReadCandidate(PluginCatalogEntry entry)
    {
        // ParseVersion never fails - it answers 0.0 for anything it cannot read - so an unusable version
        // has to be recognised by that value, or the plugin would be offered as "0.0".
        PluginReleaseEntry release = entry.Release;
        if (release is null || PluginInstaller.ParseVersion(release.Version) <= new Version(0, 0))
        {
            return null;
        }

        PluginPackageEntry package = release.FindPackageForCurrentPlatform();
        return package is { IsUsable: true } ? new PluginReleaseCandidate(entry, release, package) : null;
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

        if (catalog.IsOutdatedSchema)
        {
            Console.WriteLine($"[PluginStore] The plugin list uses schema version {catalog.SchemaVersion} and " +
                              "carries no release information; nothing can be installed from it.");
        }
        else if (catalog.SchemaVersion > PluginCatalog.SupportedSchemaVersion)
        {
            Console.WriteLine($"[PluginStore] The plugin list uses schema version {catalog.SchemaVersion}; " +
                              $"reading it as version {PluginCatalog.SupportedSchemaVersion} and ignoring what is unknown.");
        }

        _cachedCatalog = catalog;
        _catalogLoadedAt = DateTimeOffset.UtcNow;
        try
        {
            // The body is written through unchanged, so an older build reading the same file still works.
            await File.WriteAllTextAsync(Path.Combine(FileDialogHelper.GetConfigDir(), CatalogCacheFileName), json,
                cancellationToken);
            _cacheWrittenAt = DateTimeOffset.Now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[PluginStore] Could not cache the plugin catalog: {ex.Message}");
        }

        return catalog;
    }

    private PluginCatalog ReadCatalogCache()
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
                _cacheWrittenAt = new DateTimeOffset(File.GetLastWriteTime(path));
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

}
