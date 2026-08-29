using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Lists the display test's patterns as pickable entries under "Device Control", so the
/// picker offers "Key grid" instead of a text field the user has to know the tokens for.
/// Each leaf carries its token in <see cref="MenuEntry.Parameters"/>, which
/// <see cref="CommandBuilder"/> substitutes into the (hidden) <c>System.DisplayTest</c>
/// command. Merges into the same "Device Control" group as the static commands.
/// </summary>
public class DisplayTestMenuContributor(IGroupCatalog groupCatalog) : IMenuContributor
{
    private const string GroupName = "Device Control";
    private const string CommandName = "System.DisplayTest";

    /// <summary>mdi-palette — same glyph the command itself carries.</summary>
    private const string FolderIcon = "\U000F03D8";

    /// <summary>
    /// The pattern tokens, in the order the cycle runs them. Kept next to the labels so a
    /// token can never drift from what the picker inserts — the command's own parser is the
    /// only other place they appear, and it falls back to cycling on anything it cannot read.
    /// </summary>
    private static readonly (string Label, string Token, string Description)[] Patterns =
    [
        ("Cycle through all", "cycle", "Every pattern in turn, 5 s each"),
        ("Key grid", "grid", "Frame, corner blocks and centring squares per key"),
        ("Pixel ruler", "ruler", "Counts pixels inwards from all four key edges"),
        ("Colours", "color", "Pure R/G/B/W quadrants and a grey ramp"),
        ("Panel edges", "edges", "Full-panel frame, key boundaries and centring squares")
    ];

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        GroupInfo info = groupCatalog.Resolve(GroupName);

        MenuEntry group = new(GroupName, string.Empty)
        {
            Icon = info.Icon,
            Description = info.Description,
            Section = info.Section
        };

        MenuEntry folder = new("Display Test Pattern", string.Empty)
        {
            Icon = FolderIcon,
            Description = "Check the display output against the device geometry",
            Section = info.Section
        };

        foreach ((string label, string token, string description) in Patterns)
        {
            folder.Children.Add(new MenuEntry(label, CommandName)
            {
                Icon = FolderIcon,
                Description = description,
                Parameters = new Dictionary<string, string> { ["Pattern"] = token }
            });
        }

        group.Children.Add(folder);
        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }
}
