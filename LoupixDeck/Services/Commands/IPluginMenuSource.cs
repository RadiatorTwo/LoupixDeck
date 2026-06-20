using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// One plugin's deferred contribution to the command-selection menu. The plugin
/// may need network/USB I/O to assemble its groups, so the work is wrapped in a
/// <see cref="Load"/> delegate the <see cref="IMenuTreeBuilder"/> runs
/// concurrently and off the critical path.
/// </summary>
/// <param name="GroupNames">
/// The menu group(s) the plugin owns (derived from its command descriptors).
/// The builder marks these groups as loading inline until <see cref="Load"/>
/// completes.
/// </param>
public sealed record DeferredMenuSource(
    string PluginId,
    string PluginName,
    IReadOnlyList<string> GroupNames,
    Func<Task<IReadOnlyList<MenuEntry>>> Load);

/// <summary>
/// Supplies the menu builder with one <see cref="DeferredMenuSource"/> per
/// loaded plugin that contributes dynamic submenus. Unlike a plain
/// <see cref="IMenuContributor"/>, these sources are loaded individually and
/// concurrently so a slow plugin never blocks the others — or the core menu.
/// </summary>
public interface IPluginMenuSource
{
    IReadOnlyList<DeferredMenuSource> GetDeferredSources(ButtonTargets target);
}
