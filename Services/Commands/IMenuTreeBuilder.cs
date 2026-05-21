using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Builds the command-selection menu tree for a button type. Replaces the
/// per-ViewModel hard-coded <c>CreateSystemMenu</c> methods.
/// </summary>
public interface IMenuTreeBuilder
{
    Task<ObservableCollection<MenuEntry>> Build(ButtonTargets target);
}
