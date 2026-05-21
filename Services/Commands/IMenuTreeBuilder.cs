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
    /// <summary>
    /// Populates <paramref name="target"/> progressively: the core command
    /// groups are added synchronously (so they are visible the moment the
    /// dialog opens), then each plugin's groups stream in independently as the
    /// plugin finishes. The returned task completes once the synchronous core
    /// groups are in place — plugin loading continues in the background.
    /// </summary>
    Task BuildInto(ObservableCollection<MenuEntry> target, ButtonTargets buttonTarget);
}
