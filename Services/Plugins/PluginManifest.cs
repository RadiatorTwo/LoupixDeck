namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Deserialized <c>plugin.json</c> manifest that sits next to a plugin's
/// assemblies in <c>plugins/&lt;id&gt;/</c>.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Stable, filesystem-safe id; also the settings folder name.</summary>
    public string Id { get; set; }

    /// <summary>Human-readable plugin name.</summary>
    public string Name { get; set; }

    /// <summary>The plugin's own version (SemVer).</summary>
    public string Version { get; set; }

    /// <summary>SDK contract version the plugin was built against (SemVer).</summary>
    public string SdkVersion { get; set; }

    /// <summary>File name of the entry assembly within the plugin folder.</summary>
    public string EntryAssembly { get; set; }

    /// <summary>"All", "Windows" or "Linux" — the OS the plugin supports.</summary>
    public string Platform { get; set; } = "All";
}
