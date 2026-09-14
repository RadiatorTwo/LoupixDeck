namespace LoupixDeck.Services.PluginStore;

/// <summary>
/// The curated plugin list (<c>plugin-store.json</c> in the LoupixDeck repository). Only plugins listed
/// here show up in the store. Unknown fields are ignored, so newer catalogs keep loading.
/// </summary>
public sealed class PluginCatalog
{
    public int SchemaVersion { get; set; }

    public List<PluginCatalogEntry> Plugins { get; set; } = [];
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

    /// <summary>Oldest SDK version any release of the plugin needs; informational, each release's manifest decides.</summary>
    public string MinSdkVersion { get; set; }

    /// <summary>
    /// Command name prefixes the plugin owns (e.g. <c>System.Obs</c>). Lets the app recognise commands of a
    /// plugin that was never installed on this machine, e.g. in an imported config.
    /// </summary>
    public List<string> CommandPrefixes { get; set; } = [];

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
