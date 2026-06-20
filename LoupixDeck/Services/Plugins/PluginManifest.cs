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

    // ───────── Display metadata (all optional; older manifests omit them) ─────────

    /// <summary>Plugin author / publisher, shown in the plugin manager.</summary>
    public string Author { get; set; }

    /// <summary>Short, human-readable description of what the plugin does.</summary>
    public string Description { get; set; }

    /// <summary>Project / homepage URL, shown as a link in the plugin manager.</summary>
    public string ProjectUrl { get; set; }

    /// <summary>
    /// File name of an icon within the plugin folder (e.g. <c>icon.png</c>),
    /// displayed next to the plugin in the manager.
    /// </summary>
    public string IconFile { get; set; }

    /// <summary>
    /// Optional minimum host application version the plugin requires (SemVer).
    /// Informational for now; a future host-version gate can enforce it.
    /// </summary>
    public string MinHostVersion { get; set; }
}
