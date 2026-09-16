using LoupixDeck.Models;
using LoupixDeck.Services.Commands;

namespace LoupixDeck.Services.DialPresets;

/// <inheritdoc cref="IDialPresetCatalog"/>
/// <remarks>
/// Per device, because which presets are offered depends on the command registry: a command the
/// platform does not provide is not registered, and a plugin's commands are only registered on the
/// devices that enabled it. A preset naming one that is missing is not shown rather than applied
/// and then silently doing nothing. The user's presets come from the shared store, so they are the
/// same on every device.
/// </remarks>
public sealed class DialPresetCatalog : IDialPresetCatalog, IDisposable
{
    private readonly IDialPresetStore _store;
    private readonly IPluginDialPresetSource _pluginPresets;
    private readonly ICommandRegistry _registry;

    public DialPresetCatalog(IDialPresetStore store, IPluginDialPresetSource pluginPresets,
        ICommandRegistry registry)
    {
        _store = store;
        _pluginPresets = pluginPresets;
        _registry = registry;

        _store.PresetsChanged += OnStoreChanged;
        _registry.CommandsChanged += OnCommandsChanged;
    }

    /// <summary>Built-in first, then each enabled plugin's, then the user's own.</summary>
    public IReadOnlyList<DialPreset> Presets =>
        [.. DialPresetLibrary.For(_registry), .. _pluginPresets.Presets, .. _store.Presets];

    public event EventHandler PresetsChanged;

    private void OnStoreChanged(object sender, EventArgs e) => Raise();

    /// <summary>
    /// A rebuilt registry means a plugin was loaded, unloaded, enabled, disabled, installed or
    /// removed — which changes both the plugin presets on offer and which built-in ones are
    /// runnable. One signal covers every one of those transitions, on every device.
    /// </summary>
    private void OnCommandsChanged() => Raise();

    private void Raise() => PresetsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _store.PresetsChanged -= OnStoreChanged;
        _registry.CommandsChanged -= OnCommandsChanged;
    }
}
