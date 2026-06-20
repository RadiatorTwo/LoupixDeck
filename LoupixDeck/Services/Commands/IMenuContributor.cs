using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Supplies top-level command-menu groups for a given button type. The
/// <see cref="IMenuTreeBuilder"/> collects the groups from every contributor
/// and orders them. Plain command groups come from a generic contributor;
/// integrations that build dynamic submenus (OBS scenes, sensors, …) provide
/// their own contributor.
/// </summary>
public interface IMenuContributor
{
    /// <summary>
    /// Builds zero or more top-level menu groups for <paramref name="target"/>.
    /// A contributor returns an empty list when it has nothing to offer for
    /// that button type.
    /// </summary>
    Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target);
}
