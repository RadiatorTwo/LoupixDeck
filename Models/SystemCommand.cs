using System.Collections.ObjectModel;
using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

public class SystemCommand(string name, bool isCommand, string parentName = "")
{
    public string Name { get; set; } = name;
    public string ParentName { get; set; } = parentName;
    public bool IsCommand { get; set; } = isCommand;

    public ObservableCollection<SystemCommand> Childs { get; set; } = [];
}