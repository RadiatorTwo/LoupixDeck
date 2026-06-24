using System.Collections.Frozen;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Generic contributor for plain command groups (Pages, Device Control, Macros,
/// Dynamic Text, Audio, …). It lists every command of a group as a leaf,
/// filtered by <see cref="RegisteredCommand.SupportedTargets"/>. Groups that own
/// a specialized contributor (OBS, Cooler Control, Elgato, sensors) are skipped
/// here so they are not emitted twice.
/// </summary>
public class CommandGroupMenuContributor(ICommandRegistry registry) : IMenuContributor
{
    // Groups owned by a dedicated menu contributor (a plugin's IMenuContributor)
    // are skipped here so they are not also emitted as a plain command list.
    // Currently empty — all such integrations have moved into plugins.
    private static readonly FrozenSet<string> SpecializedGroups = FrozenSet.Create(StringComparer.Ordinal, []);

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        var result = new List<MenuEntry>();

        var groups = registry.GetAll()
            .Where(static c=> c.Info != null
                           && !string.IsNullOrEmpty(c.Info.Group)
                           && !SpecializedGroups.Contains(c.Info.Group)
                           && !c.HiddenFromMenu)
            .Where(c => c.SupportedTargets.HasFlag(target))
            .GroupBy(c => c.Info.Group);

        foreach (var group in groups)
        {
            var groupMenu = new MenuEntry(group.Key, string.Empty);
            foreach (var command in group)
                groupMenu.Children.Add(new MenuEntry(command.Info.DisplayName, command.CommandName));

            if (groupMenu.Children.Count > 0)
                result.Add(groupMenu);
        }

        return Task.FromResult<IReadOnlyList<MenuEntry>>(result);
    }
}
