using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;
using SdkMenuContributor = LoupixDeck.PluginSdk.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Bridges plugins that implement the SDK's <see cref="SdkMenuContributor"/>
/// into the core menu pipeline: it asks each such plugin for its dynamic
/// <see cref="MenuNode"/>s and converts them to <see cref="MenuEntry"/> trees.
/// Groups it produces are merged by name with the generic command groups, so a
/// plugin can extend its own group with dynamic submenus.
/// </summary>
public class PluginMenuContributor : IMenuContributor
{
    private readonly IPluginManager _pluginManager;

    public PluginMenuContributor(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public async Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        var result = new List<MenuEntry>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
                continue;

            if (plugin.Instance is not SdkMenuContributor contributor)
                continue;

            try
            {
                var nodes = await MenuContributorHelpers.WithTimeout(
                    contributor.GetMenuNodes(target), TimeSpan.FromSeconds(5));

                if (nodes == null)
                    continue;

                foreach (var node in nodes)
                {
                    var entry = Convert(node);
                    if (entry != null)
                        result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginMenuContributor: '{plugin.Manifest?.Id}' menu failed: {ex.Message}");
            }
        }

        return result;
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
