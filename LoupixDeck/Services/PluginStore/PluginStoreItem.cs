using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.PluginStore;

/// <summary>Where a catalog plugin stands on this machine.</summary>
public enum PluginStoreStatus
{
    /// <summary>Not installed; a compatible release can be installed.</summary>
    NotInstalled,

    /// <summary>Installed and up to date (or no newer compatible release).</summary>
    Installed,

    /// <summary>Installed by the store (or adopted) and a newer compatible release exists.</summary>
    UpdateAvailable,

    /// <summary>Copied into the plugin directory by hand; the store does not update it.</summary>
    ManuallyInstalled,

    /// <summary>Not installed; every release needs a newer SDK than this LoupixDeck provides.</summary>
    RequiresNewerApp,

    /// <summary>Not installed; the published release ships no package for this platform.</summary>
    NoRelease,

    /// <summary>The last install, update or removal only finishes on the next start.</summary>
    RestartRequired,

    /// <summary>The plugin list could not be read, or it carries no release information for the plugin.</summary>
    Unavailable
}

/// <summary>
/// The release the catalog publishes for a plugin, with the package to install on this machine. Everything
/// here comes from <c>plugin-store.json</c>; no request is needed to build it.
/// </summary>
public sealed record PluginReleaseCandidate(
    PluginCatalogEntry Entry,
    PluginReleaseEntry Release,
    PluginPackageEntry Package)
{
    public Version Version { get; } = PluginInstaller.ParseVersion(Release.Version);

    /// <summary>Release tag; recorded in the plugin's store marker and used to read the release notes.</summary>
    public string Tag => Release.TagOrDefault;

    /// <summary>File name the download is saved under.</summary>
    public string FileName => string.IsNullOrWhiteSpace(Package.FileName)
        ? $"{Entry.Id}-{Release.Version}.zip"
        : Package.FileName;

    public string DownloadUrl => Package.DownloadUrl;

    /// <summary>Expected hex SHA-256 of the package.</summary>
    public string Sha256 => Package.Sha256;
}

/// <summary>Snapshot of one catalog plugin as the store shows it.</summary>
public sealed record PluginStoreItem(
    PluginCatalogEntry Entry,
    PluginStoreStatus Status,
    LoadedPlugin Installed,
    string InstalledVersion,
    PluginReleaseCandidate Available,
    PluginReleaseCandidate Newest,
    string Error = null)
{
    /// <summary>A newer release exists but needs a newer LoupixDeck than this one.</summary>
    public bool NewerNeedsApp =>
        Newest is not null && (Available is null || Newest.Version > Available.Version)
                           && (Installed is null || Newest.Version > PluginInstaller.ParseVersion(InstalledVersion));
}

public sealed record PluginDownloadResult(string PackagePath, string Error = null)
{
    public bool Success => Error is null;
}

/// <summary>
/// One refresh of the store: the plugins, why the list is incomplete, and whether it had to come from the
/// offline copy instead of a fresh download.
/// </summary>
/// <param name="IsFromCache">The list is the copy saved on disk, so it may be out of date.</param>
/// <param name="CachedAt">When that copy was saved; null when it is not known.</param>
/// <param name="IsOutdatedSchema">The list predates the release information, so nothing can be installed.</param>
public sealed record PluginStoreResult(
    IReadOnlyList<PluginStoreItem> Items,
    string Error = null,
    bool IsFromCache = false,
    DateTimeOffset? CachedAt = null,
    bool IsOutdatedSchema = false);
