using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <inheritdoc cref="IMenuTreeBuilder"/>
public class MenuTreeBuilder : IMenuTreeBuilder
{
    /// <summary>
    /// Core groups in their fixed display order. Any group not listed here
    /// (i.e. a plugin group) is appended afterwards, sorted alphabetically.
    /// </summary>
    private static readonly string[] CoreGroupOrder =
    {
        "Pages", "Device Control", "Macros", "Dynamic Text", "Audio"
    };

    private readonly IEnumerable<IMenuContributor> _contributors;

    public MenuTreeBuilder(IEnumerable<IMenuContributor> contributors)
    {
        _contributors = contributors;
    }

    public async Task<ObservableCollection<MenuEntry>> Build(ButtonTargets target)
    {
        var groups = new List<MenuEntry>();

        foreach (var contributor in _contributors)
        {
            try
            {
                var contributed = await contributor.Contribute(target);
                if (contributed != null)
                    groups.AddRange(contributed.Where(g => g != null));
            }
            catch (Exception ex)
            {
                // A faulty contributor (e.g. a misbehaving plugin) must not
                // break the whole menu.
                Console.WriteLine($"MenuTreeBuilder: contributor '{contributor.GetType().Name}' failed: {ex.Message}");
            }
        }

        // Several contributors may emit a group with the same name (e.g. the
        // generic contributor lists OBS's static commands while the OBS plugin
        // contributes the dynamic "Scenes" submenu). Merge those into one group.
        var merged = new List<MenuEntry>();
        foreach (var group in groups)
        {
            var existing = merged.FirstOrDefault(m => m.Name == group.Name);
            if (existing == null)
            {
                merged.Add(group);
            }
            else
            {
                foreach (var child in group.Children)
                    existing.Children.Add(child);
            }
        }

        // Core groups keep their fixed slots; plugin groups sort after them,
        // alphabetically. OrderBy is stable, so equal keys keep insertion order.
        var ordered = merged
            .OrderBy(CoreGroupIndex)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

        return new ObservableCollection<MenuEntry>(ordered);
    }

    private static int CoreGroupIndex(MenuEntry group)
    {
        var idx = Array.IndexOf(CoreGroupOrder, group.Name);
        return idx >= 0 ? idx : int.MaxValue;
    }
}
