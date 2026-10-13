using LoupixDeck.Commands;
using LoupixDeck.Services.Plugins;

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

    /// <summary>
    /// The web page of a <see cref="Repository"/> value (<c>owner/name</c> on GitHub, or a full http(s)
    /// address). False for a blank value and anything that does not form an http(s) address.
    /// </summary>
    public static bool TryGetRepositoryUrl(string repository, out Uri uri)
    {
        string value = repository?.Trim().Trim('/') ?? string.Empty;
        if (value.Length == 0)
        {
            uri = null;
            return false;
        }

        string candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://github.com/" + value;
        return OpenUrlCommand.TryNormalize(candidate, out uri);
    }

    /// <summary>Optional URL of an icon image.</summary>
    public string Icon { get; set; }

    /// <summary>"Windows", "Linux" and/or "macOS"; empty means every platform.</summary>
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

    /// <summary>
    /// True when the catalog lists the running OS (or no platform at all), or when the published release ships
    /// a build this system can install: its own platform's package or the platform-independent one. False when
    /// the release ships builds for this OS, but none for this CPU architecture.
    /// </summary>
    public bool SupportsCurrentPlatform()
    {
        if (Release?.ExcludesCurrentArchitecture() == true)
        {
            return false;
        }

        if (Platforms is not { Count: > 0 })
        {
            return true;
        }

        if (Platforms.Any(p => p.Equals("All", StringComparison.OrdinalIgnoreCase)
                               || (p.Equals("Windows", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
                               || (p.Equals("Linux", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsLinux())
                               || (p.Equals("macOS", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsMacOS())))
        {
            return true;
        }

        return Release?.FindPackageForCurrentPlatform() is not null;
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
    /// The package to install on this machine: the build for the running OS and CPU architecture (e.g.
    /// <c>windows-arm64</c>), else the one for the running OS (<c>windows</c>, <c>linux</c> or <c>macos</c>),
    /// else the platform-independent one, else null when the release ships nothing for this system. A package
    /// whose <see cref="PluginPackageEntry.Architectures"/> leaves out this architecture is skipped.
    /// </summary>
    public PluginPackageEntry FindPackageForCurrentPlatform()
    {
        return FindPackage(CurrentOsPlatform, PluginArchitecture.Current);
    }

    /// <summary>
    /// True when the release ships a build for this OS (or a platform-independent one), but every such build
    /// leaves out this CPU architecture, e.g. an x64-only plugin on an ARM64 machine.
    /// </summary>
    public bool ExcludesCurrentArchitecture()
    {
        return Packages is { Count: > 0 }
               && Packages.Any(p => p != null && (p.MatchesOs(CurrentOsPlatform) || p.Matches("any")))
               && FindPackageForCurrentPlatform() is null;
    }

    internal PluginPackageEntry FindPackage(string os, string architecture)
    {
        if (Packages is not { Count: > 0 })
        {
            return null;
        }

        return Packages.FirstOrDefault(p => p != null && p.Matches($"{os}-{architecture}"))
               ?? Packages.FirstOrDefault(p => p != null && p.Matches(os) && p.Allows(architecture))
               ?? Packages.FirstOrDefault(p => p != null && p.Matches("any") && p.Allows(architecture));
    }

    private static string CurrentOsPlatform => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : "linux";
}

/// <summary>One downloadable build of a release.</summary>
public sealed class PluginPackageEntry
{
    /// <summary>
    /// <c>windows</c>, <c>linux</c>, <c>macos</c> or <c>any</c>, optionally with a CPU architecture
    /// (<c>windows-arm64</c>). An app that predates architectures only knows the plain values and skips the others.
    /// </summary>
    public string Platform { get; set; }

    /// <summary>
    /// CPU architectures the package runs on (<c>x64</c>, <c>arm64</c>). Optional: missing or empty means every
    /// architecture. Lets the plain <c>windows</c> package stay the x64 build older apps install.
    /// </summary>
    public List<string> Architectures { get; set; } = [];

    public string FileName { get; set; }

    public string DownloadUrl { get; set; }

    /// <summary>Expected hex SHA-256 of the package; a download that does not match is refused.</summary>
    public string Sha256 { get; set; }

    public bool Matches(string platform)
    {
        return string.Equals(Platform, platform, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for <paramref name="os"/> itself and for any of its architecture-specific values.</summary>
    public bool MatchesOs(string os)
    {
        return Matches(os) || (Platform?.StartsWith(os + "-", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>True when <see cref="Architectures"/> allows <paramref name="architecture"/>.</summary>
    public bool Allows(string architecture)
    {
        return PluginArchitecture.Allows(Architectures, architecture);
    }

    /// <summary>True when the entry carries everything needed to download and verify the package.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(DownloadUrl) && !string.IsNullOrWhiteSpace(Sha256);
}
