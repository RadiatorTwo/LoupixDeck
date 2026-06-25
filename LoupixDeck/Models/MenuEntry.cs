#nullable enable
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class MenuEntry(string name, string command)
{
    // TODO: Check if these need to be settable (and probably observable)
    // and if not should they be get/init only?
    public string? ParentName { get; set; }
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;

    public Dictionary<string, string> Parameters { get; set => field = value ?? new(0); } = new(0);

    public ObservableCollection<MenuEntry> Children { get; } = [];

    /// <summary>
    /// True while a slow plugin still assembles this group's dynamic submenus.
    /// The menu shows an inline "(loading…)" suffix after the group name and
    /// clears the flag once the plugin completes.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }
}
