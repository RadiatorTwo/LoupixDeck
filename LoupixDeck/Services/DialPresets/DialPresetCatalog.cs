using LoupixDeck.Models;
using LoupixDeck.Services.Commands;

namespace LoupixDeck.Services.DialPresets;

/// <inheritdoc cref="IDialPresetCatalog"/>
/// <remarks>
/// Per device, because which built-in presets are offered depends on the command registry: a
/// command the platform does not provide is not registered, and a preset naming one is not shown
/// rather than applied and then silently doing nothing. The user's presets come from the shared
/// store, so they are the same on every device.
/// </remarks>
public sealed class DialPresetCatalog : IDialPresetCatalog, IDisposable
{
    private readonly IDialPresetStore _store;
    private readonly ICommandRegistry _registry;

    public DialPresetCatalog(IDialPresetStore store, ICommandRegistry registry)
    {
        _store = store;
        _registry = registry;
        _store.PresetsChanged += OnStoreChanged;
    }

    public IReadOnlyList<DialPreset> Presets => [.. DialPresetLibrary.For(_registry), .. _store.Presets];

    public event EventHandler PresetsChanged;

    private void OnStoreChanged(object sender, EventArgs e) => PresetsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _store.PresetsChanged -= OnStoreChanged;
}
