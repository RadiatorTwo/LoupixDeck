#nullable enable
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>Outcome of attempting to load a plugin.</summary>
public enum PluginLoadStatus
{
    /// <summary>Loaded and initialized successfully; its commands are active.</summary>
    Loaded,

    /// <summary>Discovered but disabled by the user — not loaded.</summary>
    Disabled,

    /// <summary>Skipped because its SDK version is incompatible with the host.</summary>
    Incompatible,

    /// <summary>Loading or initialization threw — the plugin is inactive.</summary>
    Failed
}

/// <summary>Runtime state of one discovered plugin.</summary>
public sealed class LoadedPlugin
{
    public required PluginManifest Manifest { get; init; }

    /// <summary>Absolute path of the plugin's folder under <c>plugins/</c>.</summary>
    public required string Directory { get; init; }

    public required PluginLoadStatus Status { get; set; }

    /// <summary>Human-readable reason when <see cref="Status"/> is not Loaded.</summary>
    public string? FailureReason { get; set; }

    /// <summary>The plugin instance; null unless <see cref="Status"/> is Loaded.</summary>
    public LoupixPlugin? Instance { get; set; }

    /// <summary>The plugin's isolated load context; null unless loaded.</summary>
    internal PluginLoadContext? LoadContext { get; set; }

    /// <summary>The host bridge handed to the plugin; null unless loaded.</summary>
    public PluginHost? Host { get; set; }

    /// <summary>Commands contributed by the plugin.</summary>
    public IReadOnlyList<IPluginCommand> Commands { get; set; } = Array.Empty<IPluginCommand>();

    /// <summary>Side-strip providers contributed by the plugin (bindable to a Razer
    /// side display strip in plugin-override mode).</summary>
    public IReadOnlyList<ISideStripProvider> SideStripProviders { get; set; } =
        Array.Empty<ISideStripProvider>();
}
