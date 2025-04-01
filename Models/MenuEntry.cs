using System.Collections.ObjectModel;

namespace LoupixDeck.Models;

public class MenuEntry(string name, string command, string parentName = null)
{
    public string ParentName { get; set; } = parentName;
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;

    public ObservableCollection<MenuEntry> Childs { get; set; } = [];
}