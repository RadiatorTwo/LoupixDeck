using System.Collections.ObjectModel;
using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

public class SystemCommand(string name, Constants.SystemCommand command)
{
    public string Name { get; set; } = name;
    public Constants.SystemCommand Command { get; set; } = command;

    public ObservableCollection<SystemCommand> Childs { get; set; } = [];

    public bool DropAllowed => Command != Constants.SystemCommand.NONE;
}