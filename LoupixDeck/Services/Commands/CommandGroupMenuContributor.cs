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
public class CommandGroupMenuContributor : IMenuContributor
{
    // Groups owned by a dedicated menu contributor (a plugin's IMenuContributor)
    // are skipped here so they are not also emitted as a plain command list.
    // Currently empty — all such integrations have moved into plugins.
    private static readonly HashSet<string> SpecializedGroups = new(StringComparer.Ordinal);

    private readonly ICommandRegistry _registry;
    private readonly IDeviceService _deviceService;

    public CommandGroupMenuContributor(ICommandRegistry registry, IDeviceService deviceService)
    {
        _registry = registry;
        _deviceService = deviceService;
    }

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        var result = new List<MenuEntry>();

        // Side-display-targeting commands (e.g. per-side rotary paging) are only offered on
        // devices with separate side-display rotary areas; other devices never see them.
        var hasSideStrips = _deviceService.Device?.HasSideStrips == true;

        var groups = _registry.GetAll()
            .Where(c => c.Info != null && !string.IsNullOrEmpty(c.Info.Group))
            .Where(c => !SpecializedGroups.Contains(c.Info.Group))
            .Where(c => !c.HiddenFromMenu)
            .Where(c => !c.RequiresSideStrips || hasSideStrips)
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
