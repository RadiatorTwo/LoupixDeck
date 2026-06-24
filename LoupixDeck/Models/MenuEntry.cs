using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

[ObservableObject]
public partial class MenuEntry(string name, string command)
{
    public string? ParentName { get; set; }
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;

    public Dictionary<string, string> Parameters { get; set; } = parameters ?? [];

    public ObservableCollection<MenuEntry> Children { get; set; } = [];

    /// <summary>
    /// True while a slow plugin still assembles this group's dynamic submenus.
    /// The menu shows an inline "(loading…)" suffix after the group name and
    /// clears the flag once the plugin completes.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }
}
