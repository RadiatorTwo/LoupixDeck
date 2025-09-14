using System.Collections.ObjectModel;

namespace LoupixDeck.Models;

public class MenuEntry(string name, string command, string parentName = null, Dictionary<string, string> parameters = null)
{
    public string ParentName { get; set; } = parentName;
    public string Name { get; set; } = name;
    public string Command { get; set; } = command;
    
    public Dictionary<string, string> Parameters { get; set; } = parameters ?? [];

    public ObservableCollection<MenuEntry> Children { get; set; } = [];
}