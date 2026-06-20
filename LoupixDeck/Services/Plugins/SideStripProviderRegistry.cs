using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Flat lookup of the <see cref="ISideStripProvider"/>s contributed by all currently
/// loaded plugins. Rebuilt from <see cref="IPluginManager.Plugins"/> at startup and on
/// every plugin enable/disable/install/remove, so the editor's provider picker and the
/// controller's render path resolve a stable, current snapshot.
/// </summary>
public interface ISideStripProviderRegistry
{
    /// <summary>Immutable snapshot of all available providers.</summary>
    IReadOnlyList<ISideStripProvider> Providers { get; }

    /// <summary>Resolves a provider by its <see cref="ISideStripProvider.Id"/>
    /// (case-insensitive), or null when no provider with that id is loaded.</summary>
    ISideStripProvider Get(string id);

    /// <summary>Rebuilds the snapshot from the loaded plugins.</summary>
    void Rebuild();

    /// <summary>Raised after <see cref="Rebuild"/> swaps in a new snapshot.</summary>
    event Action ProvidersChanged;
}

/// <inheritdoc cref="ISideStripProviderRegistry"/>
public sealed class SideStripProviderRegistry : ISideStripProviderRegistry
{
    private readonly IPluginManager _pluginManager;

    // Copy-on-write snapshot, mirroring PluginManager: readers always see a
    // consistent, immutable list/map, never a torn mid-rebuild state.
    private volatile IReadOnlyList<ISideStripProvider> _providers = Array.Empty<ISideStripProvider>();
    private volatile Dictionary<string, ISideStripProvider> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    public SideStripProviderRegistry(IPluginManager pluginManager) => _pluginManager = pluginManager;

    public IReadOnlyList<ISideStripProvider> Providers => _providers;

    public event Action ProvidersChanged;

    public ISideStripProvider Get(string id) =>
        !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var provider) ? provider : null;

    public void Rebuild()
    {
        var list = new List<ISideStripProvider>();
        var map = new Dictionary<string, ISideStripProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in _pluginManager.Plugins.Where(p => p.Status == PluginLoadStatus.Loaded))
        {
            foreach (var provider in plugin.SideStripProviders)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                    continue;

                if (!map.TryAdd(provider.Id, provider))
                {
                    Console.WriteLine(
                        $"SideStripProviderRegistry: duplicate provider id '{provider.Id}' ignored.");
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
