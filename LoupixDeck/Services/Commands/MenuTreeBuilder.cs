using System.Collections.ObjectModel;
using Avalonia.Threading;
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
        "Pages", "Device Control", "Macros", "User Macros", "Dynamic Text", "Audio"
    };

    /// <summary>How long a single plugin may take before its menu is skipped.</summary>
    private static readonly TimeSpan PluginTimeout = TimeSpan.FromSeconds(5);

    private readonly IEnumerable<IMenuContributor> _contributors;
    private readonly IPluginMenuSource _pluginMenuSource;
    private readonly IGroupCatalog _groupCatalog;

    public MenuTreeBuilder(IEnumerable<IMenuContributor> contributors, IPluginMenuSource pluginMenuSource, IGroupCatalog groupCatalog)
    {
        _contributors = contributors;
        _pluginMenuSource = pluginMenuSource;
        _groupCatalog = groupCatalog;
    }

    public async Task BuildInto(ObservableCollection<MenuEntry> target, ButtonTargets buttonTarget)
    {
        // ── Phase 1: core groups, synchronous — visible immediately ──
        foreach (var contributor in _contributors)
        {
            try
            {
                var contributed = await contributor.Contribute(buttonTarget);
                if (contributed == null)
                    continue;

                foreach (var group in contributed.Where(g => g != null))
                    MergeGroup(target, group);
            }
            catch (Exception ex)
            {
                // A faulty contributor must not break the whole menu.
                Console.WriteLine($"MenuTreeBuilder: contributor '{contributor.GetType().Name}' failed: {ex.Message}");
            }
        }

        // ── Phase 2: plugin groups, deferred and concurrent ──
        // Each plugin's group is shown right away with an inline "(loading…)"
        // suffix; the dynamic submenus fill in once the (possibly slow) plugin
        // completes, and the suffix is cleared.
        var sources = _pluginMenuSource.GetDeferredSources(buttonTarget);
        foreach (var source in sources)
        {
            foreach (var groupName in source.GroupNames)
            {
                var group = target.FirstOrDefault(g => g.Name == groupName);
                if (group == null)
                {
                    var groupInfo = _groupCatalog.Resolve(groupName);
                    group = new MenuEntry(groupName, string.Empty)
                    {
                        Icon = groupInfo.Icon,
                        Description = groupInfo.Description,
                        Section = groupInfo.Section
                    };
                    MergeGroup(target, group);
                }

                group.IsLoading = true;
            }

            // Fire-and-forget: the dialog is already on screen and the bound
            // collection updates live as each plugin finishes.
            _ = LoadPluginGroups(target, source);
        }
    }

    private static async Task LoadPluginGroups(
        ObservableCollection<MenuEntry> target, DeferredMenuSource source)
    {
        IReadOnlyList<MenuEntry> groups = [];
        try
        {
            groups = await MenuContributorHelpers.WithTimeout(source.Load(), PluginTimeout);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MenuTreeBuilder: plugin '{source.PluginId}' menu failed: {ex.Message}");
        }

        // The bound collection may only be mutated on the UI thread.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var group in groups.Where(g => g != null))
                MergeGroup(target, group);

            // Clear the loading state. A group that stayed an empty stub keeps its
            // card and gets a non-actionable info row instead of being removed: a
            // plugin whose commands are all hidden (it contributes only dynamic
            // submenus) would otherwise vanish from the picker entirely, leaving no
            // hint that it is loaded but currently has nothing to offer.
            foreach (var groupName in source.GroupNames)
            {
                var group = target.FirstOrDefault(g => g.Name == groupName);
                if (group == null)
                    continue;

                group.IsLoading = false;
                if (group.Children.Count == 0 && CoreGroupIndex(group) == int.MaxValue)
                    group.Children.Add(EmptyGroupNotice(source.PluginName));
            }
        });
    }

    /// <summary>
    /// The placeholder leaf shown inside a plugin group that contributed nothing.
    /// It carries no command and no children, so the picker classifies it as a
    /// non-actionable info row: visible, but neither selectable nor insertable.
    /// </summary>
    private static MenuEntry EmptyGroupNotice(string pluginName) =>
        new("Nothing to show", string.Empty)
        {
            Description = $"'{pluginName}' is loaded but currently offers no commands."
        };

    /// <summary>
    /// Adds <paramref name="group"/> to <paramref name="target"/>: if a group
    /// with the same name already exists its children are appended, otherwise
    /// the group is inserted at its ordered slot (core groups keep fixed
    /// positions, everything else sorts alphabetically after them).
    /// </summary>
    private static void MergeGroup(ObservableCollection<MenuEntry> target, MenuEntry group)
    {
        var existing = target.FirstOrDefault(m => m.Name == group.Name);
        if (existing != null)
        {
            foreach (var child in group.Children)
                existing.Children.Add(child);
            return;
        }

        var index = 0;
        while (index < target.Count && Compare(target[index], group) <= 0)
            index++;

        target.Insert(index, group);
    }

    /// <summary>Orders groups: core groups by fixed slot, the rest alphabetically.</summary>
    private static int Compare(MenuEntry a, MenuEntry b)
    {
        var byCore = CoreGroupIndex(a).CompareTo(CoreGroupIndex(b));
        if (byCore != 0)
            return byCore;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int CoreGroupIndex(MenuEntry group)
    {
        var idx = Array.IndexOf(CoreGroupOrder, group.Name);
        return idx >= 0 ? idx : int.MaxValue;
    }
}