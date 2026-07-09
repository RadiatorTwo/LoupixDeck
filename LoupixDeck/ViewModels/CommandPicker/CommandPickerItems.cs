using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.CommandPicker;

/// <summary>
/// Classifies a <see cref="MenuEntry"/> for the card picker. The host menu tree is
/// arbitrarily deep (e.g. Audio: <c>Audio → device → commands</c>), so an entry is
/// either an insertable <em>command</em>, an expandable <em>group</em>, or a
/// non-actionable info row (e.g. "OBS not connected").
/// </summary>
public static class MenuEntryClassifier
{
    /// <summary>True when the entry inserts something — a normal command leaf or a rotary command group.</summary>
    public static bool IsCommand(this MenuEntry e) =>
        e.IsCommandGroup || !string.IsNullOrEmpty(e.Command);

    /// <summary>True when the entry is an expandable group: it carries no command of its own but has children.</summary>
    public static bool IsGroup(this MenuEntry e) =>
        !e.IsCommand() && e.Children.Count > 0;

    /// <summary>Total number of insertable commands in this entry's subtree (recursively).</summary>
    public static int CommandCount(MenuEntry entry)
    {
        int count = 0;
        foreach (MenuEntry child in entry.Children)
        {
            if (child.IsGroup())
                count += CommandCount(child);
            else if (child.IsCommand())
                count++;
        }

        return count;
    }

    /// <summary>
    /// Projects a parent entry's children into detail-area nodes in source order:
    /// commands become <see cref="CommandRowViewModel"/>, groups become
    /// <see cref="CommandGroupNodeViewModel"/> (which recurses on its own children).
    /// If exactly one group sits on this level it is auto-expanded.
    /// </summary>
    public static void Project(MenuEntry parent, string fallbackIcon, ObservableCollection<object> into)
    {
        CommandGroupNodeViewModel onlyGroup = null;
        int groupCount = 0;

        foreach (MenuEntry child in parent.Children)
        {
            if (child.IsGroup())
            {
                var node = new CommandGroupNodeViewModel(child, fallbackIcon);
                into.Add(node);
                onlyGroup = node;
                groupCount++;
            }
            else
            {
                into.Add(new CommandRowViewModel(child, fallbackIcon));
            }
        }

        // A lone group is expanded by default; several groups start collapsed.
        if (groupCount == 1)
            onlyGroup.IsExpanded = true;
    }
}

/// <summary>
/// One command row in the picker's detail list, wrapping the leaf
/// <see cref="MenuEntry"/> that carries the assignment payload.
/// </summary>
public partial class CommandRowViewModel : ViewModelBase
{
    /// <summary>The underlying menu leaf — the insertion payload handed to the host.</summary>
    public MenuEntry Entry { get; }

    public string Title => Entry.Name;
    public string Description => Entry.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Entry.Description);

    /// <summary>True when this row inserts a rotary command group (fills several
    /// rotary slots at once) rather than a single command — badged in the picker.</summary>
    public bool IsCommandGroup => Entry.IsCommandGroup;

    /// <summary>Resolved row glyph: the command's own icon, else the category icon.</summary>
    public string Icon { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public CommandRowViewModel(MenuEntry entry, string fallbackIcon)
    {
        Entry = entry;
        Icon = string.IsNullOrEmpty(entry.Icon) ? fallbackIcon : entry.Icon;
    }
}

/// <summary>
/// One expandable group in the detail list (e.g. an Audio device holding its Volume
/// Up / Volume Down / Mute commands). It is an accordion header, never inserted as a
/// command; its <see cref="Children"/> hold command rows and further nested groups.
/// </summary>
public partial class CommandGroupNodeViewModel : ViewModelBase
{
    /// <summary>mdi-folder — fallback glyph for a group without a declared icon.</summary>
    private const string GroupFallbackIcon = "\U000F024B";

    /// <summary>The underlying group menu entry.</summary>
    public MenuEntry Group { get; }

    public string Title => Group.Name;
    public string Description => Group.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Group.Description);
    public string Icon { get; }

    /// <summary>Insertable commands contained anywhere below this group.</summary>
    public int Count => MenuEntryClassifier.CommandCount(Group);

    /// <summary>Command rows and nested group nodes shown when the group is expanded.</summary>
    public ObservableCollection<object> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public CommandGroupNodeViewModel(MenuEntry group, string fallbackIcon)
    {
        Group = group;
        Icon = string.IsNullOrEmpty(group.Icon) ? GroupFallbackIcon : group.Icon;

        // A nested command inherits this group's icon as its row fallback.
        MenuEntryClassifier.Project(group, Icon, Children);
    }
}

/// <summary>
/// One category card, wrapping a top-level group <see cref="MenuEntry"/>. Its detail
/// contents (commands and nested groups) are projected once into <see cref="DetailNodes"/>,
/// so expand/collapse state survives re-selecting the card while the picker is open.
/// </summary>
public partial class CommandCategoryViewModel : ViewModelBase
{
    /// <summary>The underlying top-level group menu entry.</summary>
    public MenuEntry Group { get; }

    public string Title => Group.Name;
    public string Description => Group.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Group.Description);
    public string Icon { get; }

    /// <summary>Total insertable commands in this category, counting nested groups.</summary>
    public int Count => MenuEntryClassifier.CommandCount(Group);

    /// <summary>Detail-area content: direct command rows and expandable group nodes.</summary>
    public ObservableCollection<object> DetailNodes { get; } = [];

    public bool IsLoading => Group.IsLoading;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public CommandCategoryViewModel(MenuEntry group, string icon)
    {
        Group = group;
        Icon = icon;

        MenuEntryClassifier.Project(group, icon, DetailNodes);
    }

    /// <summary>Raises change notifications for count/loading after the source group updates.</summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsLoading));
    }
}

/// <summary>A collapsible section (Core / Macros / Plugins) holding category cards.</summary>
public partial class CommandSectionViewModel : ViewModelBase
{
    public CommandGroupSection Section { get; }
    public string Title { get; }
    public string Icon { get; }

    public ObservableCollection<CommandCategoryViewModel> Categories { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public CommandSectionViewModel(CommandGroupSection section, string title, string icon)
    {
        Section = section;
        Title = title;
        Icon = icon;
    }
}

/// <summary>A search-result group: the matching commands sharing one folder path.</summary>
public sealed class CommandSearchGroupViewModel(string title, string icon, IEnumerable<CommandRowViewModel> commands)
{
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public ObservableCollection<CommandRowViewModel> Commands { get; } = new(commands);
}
