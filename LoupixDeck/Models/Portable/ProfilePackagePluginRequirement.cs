namespace LoupixDeck.Models.Portable;

/// <summary>
/// A plugin the exported subtree depends on, captured at export time from the plugin's
/// <c>plugin.json</c>. This is the only source of metadata for a plugin the importing machine
/// does not have installed — without it the import preview could only show a bare command name.
/// </summary>
public sealed class ProfilePackagePluginRequirement
{
    /// <summary>Stable plugin id (matches <c>PluginManifest.Id</c>).</summary>
    public string Id { get; set; }

    /// <summary>Human-readable plugin name as shown in the plugin manager.</summary>
    public string Name { get; set; }

    /// <summary>The plugin version present on the exporting machine (informational).</summary>
    public string Version { get; set; }

    /// <summary>Project / download URL, offered as a link when the plugin is missing.</summary>
    public string ProjectUrl { get; set; }
}
