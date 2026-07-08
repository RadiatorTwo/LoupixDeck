using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.CommandPicker;

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
/// One category card, wrapping a group <see cref="MenuEntry"/> and exposing its
/// commands as rows for the detail list.
/// </summary>
public partial class CommandCategoryViewModel : ViewModelBase
{
    /// <summary>The underlying group menu entry.</summary>
    public MenuEntry Group { get; }

    public string Title => Group.Name;
    public string Description => Group.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Group.Description);
    public string Icon { get; }

    public ObservableCollection<CommandRowViewModel> Commands { get; } = [];

    public int Count => Commands.Count;

    public bool IsLoading => Group.IsLoading;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public CommandCategoryViewModel(MenuEntry group, string icon)
    {
        Group = group;
        Icon = icon;

        foreach (var child in group.Children)
            Commands.Add(new CommandRowViewModel(child, icon));
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

/// <summary>A search-result group: the matching commands of one category.</summary>
public sealed class CommandSearchGroupViewModel(string title, string icon, IEnumerable<CommandRowViewModel> commands)
{
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public ObservableCollection<CommandRowViewModel> Commands { get; } = new(commands);
}