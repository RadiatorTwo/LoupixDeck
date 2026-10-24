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
    private readonly Models.LoupedeckConfig _config;

    public PluginMenuContributor(
        IPluginManager pluginManager,
        ICommandBuilder commandBuilder,
        Models.LoupedeckConfig config)
    {
        _pluginManager = pluginManager;
        _commandBuilder = commandBuilder;
        _config = config;
    }

    public IReadOnlyList<DeferredMenuSource> GetDeferredSources(ButtonTargets target)
    {
        var sources = new List<DeferredMenuSource>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
                continue;

            // Plugins are shared across devices; only surface a plugin's menu on the
            // device that enabled it, so its commands aren't shown (and can't be
            // assigned) on a device where the plugin is inactive (issue #163).
            if (!PluginEnabledForDevice(plugin.Manifest?.Id))
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
                var result = new List<MenuEntry>();

                // Plugins have only ever been asked for one button type, and some compare it
                // with ==. A combined target (the action panel's) is therefore asked one type at
                // a time and the answers merged.
                foreach (var single in SingleTargets(target))
                {
                    var nodes = await contributor.GetMenuNodes(single);
                    if (nodes == null)
                        continue;

                    foreach (var node in nodes)
                    {
                        var entry = Convert(node, single, pluginId);
                        if (entry != null)
                            MergeInto(result, entry);
                    }
                }

                return result;
            }));
        }

        return sources;
    }

    /// <summary>True when this device's config enables the plugin with the given id.</summary>
    private bool PluginEnabledForDevice(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        var enabled = _config?.EnabledPlugins;
        return enabled != null
               && enabled.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
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

    private static readonly ButtonTargets[] SingleTargetOrder =
        [ButtonTargets.TouchButton, ButtonTargets.SimpleButton, ButtonTargets.RotaryEncoder];

    /// <summary>The individual button types <paramref name="target"/> covers; a single type or
    /// <see cref="ButtonTargets.None"/> is passed through unchanged.</summary>
    private static IReadOnlyList<ButtonTargets> SingleTargets(ButtonTargets target)
    {
        var singles = SingleTargetOrder.Where(t => target.HasFlag(t)).ToList();
        return singles.Count > 1 ? singles : [target];
    }

    /// <summary>
    /// Adds <paramref name="entry"/> to <paramref name="siblings"/>, folding it into an entry that
    /// is the same command or submenu, so a node offered for several button types appears once.
    /// </summary>
    private static void MergeInto(IList<MenuEntry> siblings, MenuEntry entry)
    {
        var existing = siblings.FirstOrDefault(e => SameEntry(e, entry));
        if (existing == null)
        {
            siblings.Add(entry);
            return;
        }

        foreach (var child in entry.Children)
            MergeInto(existing.Children, child);
    }

    private static bool SameEntry(MenuEntry a, MenuEntry b) =>
        a.Name == b.Name
        && a.Command == b.Command
        && a.IsCommandGroup == b.IsCommandGroup
        && a.Parameters.Count == b.Parameters.Count
        && !a.Parameters.Except(b.Parameters).Any();

    private MenuEntry Convert(MenuNode node, ButtonTargets target, string pluginId)
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

            var map = RotaryGroupBuilder.Build(node.RotaryGroup, _commandBuilder);
            if (map.Count == 0)
                return null;

            return new MenuEntry(node.Name, string.Empty) { RotaryGroup = map, OwnerPluginId = pluginId };
        }

        var parameters = node.Parameters is { Count: > 0 }
            ? new Dictionary<string, string>(node.Parameters)
            : null;

        var entry = new MenuEntry(node.Name, node.CommandName ?? string.Empty, null, parameters)
        {
            OwnerPluginId = pluginId
        };

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var converted = Convert(child, target, pluginId);
                if (converted != null)
                    entry.Children.Add(converted);
            }
        }

        return entry;
    }
}
