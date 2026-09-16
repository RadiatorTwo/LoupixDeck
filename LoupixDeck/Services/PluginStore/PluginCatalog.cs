namespace LoupixDeck.Services.PluginStore;

/// <summary>
/// The curated plugin list (<c>plugin-store.json</c> in the LoupixDeck repository). Only plugins listed
/// here show up in the store. Unknown fields are ignored, so newer catalogs keep loading.
/// </summary>
public sealed class PluginCatalog
{
    /// <summary>
    /// The schema this app understands. Schema 2 carries the published release of every plugin, so a refresh
    /// is a single request; an older list has no release information and nothing can be installed from it.
    /// A newer list still loads — unknown fields are ignored.
    /// </summary>
    public const int SupportedSchemaVersion = 2;

    public int SchemaVersion { get; set; }

    public List<PluginCatalogEntry> Plugins { get; set; } = [];

    /// <summary>True when the list predates the release information (schema 1, or none at all).</summary>
    public bool IsOutdatedSchema => SchemaVersion < SupportedSchemaVersion;
}

/// <summary>One plugin in the catalog.</summary>
public sealed class PluginCatalogEntry
{
    /// <summary>Stable plugin id; matches the <c>id</c> of the plugin's <c>plugin.json</c>.</summary>
    public string Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public string Author { get; set; }

    /// <summary>GitHub repository as <c>owner/name</c>; its stable releases are the plugin's versions.</summary>
    public string Repository { get; set; }

    /// <summary>Optional URL of an icon image.</summary>
    public string Icon { get; set; }

    /// <summary>"Windows" and/or "Linux"; empty means every platform.</summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>
    /// Oldest SDK version any release of the plugin needs; informational only — whether the published release
    /// can be loaded is decided by <see cref="PluginReleaseEntry.SdkVersion"/>.
    /// </summary>
    public string MinSdkVersion { get; set; }

    /// <summary>
    /// Command name prefixes the plugin owns (e.g. <c>System.Obs</c>). Lets the app recognise commands of a
    /// plugin that was never installed on this machine, e.g. in an imported config.
    /// </summary>
    public List<string> CommandPrefixes { get; set; } = [];

    /// <summary>
    /// The published release of this plugin (schema 2). Null in an older list — the plugin is then shown, but
    /// nothing is offered for it: the app never asks the plugin's repository what its versions are.
    /// </summary>
    public PluginReleaseEntry Release { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    /// <summary>True when the catalog lists the running OS (or no platform at all).</summary>
    public bool SupportsCurrentPlatform()
    {
        if (Platforms is not { Count: > 0 })
        {
            return true;
        }

        return Platforms.Any(p => p.Equals("All", StringComparison.OrdinalIgnoreCase)
                                  || (p.Equals("Windows", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
                                  || (p.Equals("Linux", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsLinux()));
    }

    /// <summary>True when <paramref name="commandName"/> starts with one of <see cref="CommandPrefixes"/>.</summary>
    public bool OwnsCommand(string commandName)
    {
        return !string.IsNullOrEmpty(commandName) && CommandPrefixes != null
                                                  && CommandPrefixes.Any(p => !string.IsNullOrEmpty(p)
                                                                              && commandName.StartsWith(p, StringComparison.Ordinal));
    }
}

/// <summary>
/// The published release of a catalog plugin. The catalog is the only source of this information: the app
/// compares it against the installed version locally and never asks the plugin's repository.
/// </summary>
public sealed class PluginReleaseEntry
{
    /// <summary>Plain <c>major.minor.patch</c>; the same value the package's <c>plugin.json</c> carries.</summary>
    public string Version { get; set; }

    /// <summary>GitHub tag of the release; <c>v&lt;version&gt;</c> when the list omits it.</summary>
    public string Tag { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>SDK version the release was built against; decides whether this app can load it.</summary>
    public string SdkVersion { get; set; }

    /// <summary>Page of the release, shown when its notes cannot be read.</summary>
    public string ReleaseNotesUrl { get; set; }

    /// <summary>The downloadable builds of this release, one per platform.</summary>
    public List<PluginPackageEntry> Packages { get; set; } = [];

    /// <summary>The tag as published, or the conventional one built from <see cref="Version"/>.</summary>
    public string TagOrDefault => string.IsNullOrWhiteSpace(Tag)
        ? (string.IsNullOrWhiteSpace(Version) ? null : $"v{Version}")
        : Tag;

    /// <summary>
    /// The package to install on this machine: the build for the running OS, else the platform-independent
    /// one, else null when the release ships nothing for this system.
    /// </summary>
    public PluginPackageEntry FindPackageForCurrentPlatform()
    {
        if (Packages is not { Count: > 0 })
        {
            return null;
        }

        string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        return Packages.FirstOrDefault(p => p != null && p.Matches(platform))
               ?? Packages.FirstOrDefault(p => p != null && p.Matches("any"));
    }
}

/// <summary>One downloadable build of a release.</summary>
public sealed class PluginPackageEntry
{
    /// <summary><c>windows</c>, <c>linux</c> or <c>any</c>.</summary>
    public string Platform { get; set; }

    public string FileName { get; set; }

    public string DownloadUrl { get; set; }

    /// <summary>Expected hex SHA-256 of the package; a download that does not match is refused.</summary>
    public string Sha256 { get; set; }

    public bool Matches(string platform)
    {
        return string.Equals(Platform, platform, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the entry carries everything needed to download and verify the package.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(DownloadUrl) && !string.IsNullOrWhiteSpace(Sha256);
}
