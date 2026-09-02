using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Flat lookup of the <see cref="IScreensaverProvider"/>s contributed by all currently loaded
/// plugins (issue #124). Rebuilt from <see cref="IPluginManager.Plugins"/> at startup and on every
/// plugin enable/disable/install/remove, so the settings picker and the screensaver manager resolve
/// a stable, current snapshot. Mirrors <see cref="ISideStripProviderRegistry"/>.
/// </summary>
public interface IScreensaverProviderRegistry
{
    /// <summary>Immutable snapshot of all available providers.</summary>
    IReadOnlyList<IScreensaverProvider> Providers { get; }

    /// <summary>Resolves a provider by its <see cref="IScreensaverProvider.Id"/>
    /// (case-insensitive), or null when no provider with that id is loaded.</summary>
    IScreensaverProvider Get(string id);

    /// <summary>Rebuilds the snapshot from the loaded plugins.</summary>
    void Rebuild();

    /// <summary>Raised after <see cref="Rebuild"/> swaps in a new snapshot.</summary>
    event Action ProvidersChanged;
}

/// <inheritdoc cref="IScreensaverProviderRegistry"/>
public sealed class ScreensaverProviderRegistry : IScreensaverProviderRegistry
{
    private readonly IPluginManager _pluginManager;

    // Copy-on-write snapshot, mirroring PluginManager: readers always see a
    // consistent, immutable list/map, never a torn mid-rebuild state.
    private volatile IReadOnlyList<IScreensaverProvider> _providers = Array.Empty<IScreensaverProvider>();
    private volatile Dictionary<string, IScreensaverProvider> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    public ScreensaverProviderRegistry(IPluginManager pluginManager) => _pluginManager = pluginManager;

    public IReadOnlyList<IScreensaverProvider> Providers => _providers;

    public event Action ProvidersChanged;

    public IScreensaverProvider Get(string id) =>
        !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var provider) ? provider : null;

    public void Rebuild()
    {
        var list = new List<IScreensaverProvider>();
        var map = new Dictionary<string, IScreensaverProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in _pluginManager.Plugins.Where(p => p.Status == PluginLoadStatus.Loaded))
        {
            foreach (var provider in plugin.ScreensaverProviders)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                    continue;

                if (!map.TryAdd(provider.Id, provider))
                {
                    Console.WriteLine(
                        $"ScreensaverProviderRegistry: duplicate provider id '{provider.Id}' ignored.");
                    continue;
                }

                list.Add(provider);
            }
        }

        _providers = list;
        _byId = map;
        ProvidersChanged?.Invoke();
    }
}
