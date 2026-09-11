using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.DialPresets;
// Both the app and the plugin SDK define IMenuContributor — this contributor implements the app-side one.
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Lists every dial preset as a leaf in a dedicated "Dial Presets" group. Each leaf carries the
/// preset's three commands as a <see cref="MenuEntry.RotaryGroup"/>, which is the shape the rotary
/// button editor and the panel assignment already know how to apply, so selecting one fills all
/// three gestures at once.
/// </summary>
/// <remarks>
/// Offered on rotary encoders only. A preset means nothing on a touch or LED button, and the same
/// guard is what <c>PluginMenuContributor</c> applies to a plugin's rotary groups.
/// </remarks>
public class DialPresetMenuContributor : IMenuContributor
{
    public const string GroupName = "Dial Presets";

    private readonly IDialPresetCatalog _catalog;
    private readonly IGroupCatalog _groupCatalog;

    public DialPresetMenuContributor(IDialPresetCatalog catalog, IGroupCatalog groupCatalog)
    {
        _catalog = catalog;
        _groupCatalog = groupCatalog;
    }

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        if (!target.HasFlag(ButtonTargets.RotaryEncoder))
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        IReadOnlyList<DialPreset> presets = _catalog.Presets;
        if (presets.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        GroupInfo info = _groupCatalog.Resolve(GroupName);
        MenuEntry group = new(GroupName, string.Empty)
        {
            Icon = info.Icon,
            Description = info.Description,
            Section = info.Section
        };

        foreach (DialPreset preset in presets)
        {
            group.Children.Add(new MenuEntry(preset.Name, string.Empty)
            {
                Icon = string.IsNullOrEmpty(preset.Glyph) ? info.Icon : preset.Glyph,
                Description = DescribeGestures(preset),
                RotaryGroup = preset.ToRotaryGroup()
            });
        }

        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }

    /// <summary>Which gestures the preset fills, so a preset that leaves one alone says so before
    /// it is applied rather than after.</summary>
    private static string DescribeGestures(DialPreset preset)
    {
        List<string> filled = [];

        if (!string.IsNullOrWhiteSpace(preset.Left))
            filled.Add("rotate left");
        if (!string.IsNullOrWhiteSpace(preset.Right))
            filled.Add("rotate right");
        if (!string.IsNullOrWhiteSpace(preset.Press))
            filled.Add("press");

        return $"Sets {string.Join(", ", filled)}";
    }
}
