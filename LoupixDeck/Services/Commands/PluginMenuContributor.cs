using System.Collections.Immutable;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using SdkMenuContributor = LoupixDeck.PluginSdk.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Bridges plugins that implement the SDK's <see cref="SdkMenuContributor"/>
/// into the core menu pipeline. It hands the <see cref="MenuTreeBuilder"/> one
/// <see cref="DeferredMenuSource"/> per such plugin; the builder loads them
/// concurrently and merges the resulting <see cref="MenuEntry"/> trees once
/// they arrive, so a slow/offline integration cannot block the menu.
/// </summary>
public class PluginMenuContributor : IPluginMenuSource
{
    private readonly IPluginManager _pluginManager;

    public PluginMenuContributor(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IReadOnlyList<DeferredMenuSource> GetDeferredSources(ButtonTargets target)
    {
        var sources = new List<DeferredMenuSource>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
                continue;

            if (plugin.Instance is not SdkMenuContributor contributor)
                continue;

            var pluginId = plugin.Manifest?.Id ?? plugin.Instance.GetType().Name;
            var pluginName = plugin.Manifest?.Name ?? pluginId;

            // The plugin's command groups are the anchors for the inline
            // "(loading…)" indicator shown while its dynamic submenus load.
            var groupNames = SafeGetGroupNames(plugin.Instance);

            sources.Add(new DeferredMenuSource(pluginId, pluginName, groupNames, async () =>
            {
                var nodes = await contributor.GetMenuNodes(target);
                var result = new List<MenuEntry>();

                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var entry = Convert(node);
                        if (entry != null)
                            result.Add(entry);
                    }
                }

                return result;
            }));
        }

        return sources;
    }

    private static ImmutableList<string> SafeGetGroupNames(LoupixPlugin plugin)
    {
        try
        {
            return plugin.GetCommands()
                .Where(static c => c?.Descriptor != null && !string.IsNullOrWhiteSpace(c.Descriptor.Group))
                .Select(static c => c.Descriptor.Group)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginMenuContributor: failed to read groups of '{plugin.Metadata?.Id}': {ex.Message}");
            return ImmutableList<string>.Empty;
        }
    }

    private static MenuEntry Convert(MenuNode node)
    {
        if (node == null)
            return null;

        var parameters = node.Parameters is { Count: > 0 }
            ? new Dictionary<string, string>(node.Parameters)
            : null;

        var entry = new MenuEntry(node.Name, node.CommandName ?? string.Empty, null, parameters);

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var converted = Convert(child);
                if (converted != null)
                    entry.Children.Add(converted);
            }
        }

        return entry;
    }
}
