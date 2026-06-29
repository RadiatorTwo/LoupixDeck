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
    private readonly ICommandBuilder _commandBuilder;

    public PluginMenuContributor(IPluginManager pluginManager, ICommandBuilder commandBuilder)
    {
        _pluginManager = pluginManager;
        _commandBuilder = commandBuilder;
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
                        var entry = Convert(node, target);
                        if (entry != null)
                            result.Add(entry);
                    }
                }

                return result;
            }));
        }

        return sources;
    }

    private static IReadOnlyList<string> SafeGetGroupNames(LoupixPlugin plugin)
    {
        try
        {
            return plugin.GetCommands()
                .Where(c => c?.Descriptor != null && !string.IsNullOrWhiteSpace(c.Descriptor.Group))
                .Select(c => c.Descriptor.Group)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginMenuContributor: failed to read groups of '{plugin.Metadata?.Id}': {ex.Message}");
            return [];
        }
    }

    private MenuEntry Convert(MenuNode node, ButtonTargets target)
    {
        if (node == null)
            return null;

        // A rotary command group only makes sense on a rotary encoder. For any
        // other target the group is ignored; such a node carries no command or
        // children, so it produces nothing and is dropped.
        if (node.RotaryGroup is { Count: > 0 })
        {
            if (!target.HasFlag(ButtonTargets.RotaryEncoder))
                return null;

            var map = BuildRotaryGroup(node.RotaryGroup);
            if (map.Count == 0)
                return null;

            return new MenuEntry(node.Name, string.Empty) { RotaryGroup = map };
        }

        var parameters = node.Parameters is { Count: > 0 }
            ? new Dictionary<string, string>(node.Parameters)
            : null;

        var entry = new MenuEntry(node.Name, node.CommandName ?? string.Empty, null, parameters);

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var converted = Convert(child, target);
                if (converted != null)
                    entry.Children.Add(converted);
            }
        }

        return entry;
    }

    /// <summary>
    /// Builds the per-action raw command strings for a rotary group, reusing the
    /// same command-string builder used for normal menu leaves so parameter
    /// templates are filled identically. Actions whose command does not resolve
    /// are dropped.
    /// </summary>
    private Dictionary<RotaryAction, string> BuildRotaryGroup(
        IReadOnlyDictionary<RotaryAction, MenuCommandRef> group)
    {
        var map = new Dictionary<RotaryAction, string>();

        foreach (var (action, reference) in group)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.CommandName))
                continue;

            var parameters = reference.Parameters is { Count: > 0 }
                ? new Dictionary<string, string>(reference.Parameters)
                : null;

            var raw = _commandBuilder.CreateCommandFromMenuEntry(
                new MenuEntry(reference.CommandName, reference.CommandName, null, parameters));

            if (!string.IsNullOrWhiteSpace(raw))
                map[action] = raw;
        }

        return map;
    }
}
