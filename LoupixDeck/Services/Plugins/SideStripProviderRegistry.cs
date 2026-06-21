using System.Collections.Immutable;
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
public sealed class SideStripProviderRegistry(IPluginManager pluginManager) : ISideStripProviderRegistry
{
    // Copy-on-write snapshot, mirroring PluginManager: readers always see a
    // consistent, immutable list/map, never a torn mid-rebuild state.
    private ImmutableArray<ISideStripProvider> providers = [];
    private ImmutableDictionary<string, ISideStripProvider> _byId = ImmutableDictionary.Create<string, ISideStripProvider>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ISideStripProvider> Providers => providers;

    public event Action ProvidersChanged;

    public ISideStripProvider Get(string id) =>
        !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var provider) ? provider : null;

    public void Rebuild()
    {
        var list = ImmutableArray.CreateBuilder<ISideStripProvider>();
        var map = ImmutableDictionary.CreateBuilder<string, ISideStripProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in pluginManager.Plugins.Where(p => p.Status == PluginLoadStatus.Loaded))
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

        providers = list.DrainToImmutable();
        _byId = map.ToImmutable();
        ProvidersChanged?.Invoke();
    }
}
