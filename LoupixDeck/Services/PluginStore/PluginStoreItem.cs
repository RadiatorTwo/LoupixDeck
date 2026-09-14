using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Updates;

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

    /// <summary>Not installed; no release ships a package for this platform.</summary>
    NoRelease,

    /// <summary>The last install, update or removal only finishes on the next start.</summary>
    RestartRequired,

    /// <summary>The plugin's releases could not be read (offline, rate limit).</summary>
    Unavailable
}

/// <summary>A release of a plugin that ships a package for this platform and a verifiable checksum.</summary>
/// <param name="Manifest">The release's <c>plugin.json</c> asset.</param>
/// <param name="Package">The zip for this platform.</param>
/// <param name="Sha256">Expected hex SHA-256 of <paramref name="Package"/>.</param>
public sealed record PluginReleaseCandidate(
    ReleaseInfo Release,
    PluginManifest Manifest,
    ReleaseAsset Package,
    string Sha256)
{
    public Version Version => Release.Version;
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
