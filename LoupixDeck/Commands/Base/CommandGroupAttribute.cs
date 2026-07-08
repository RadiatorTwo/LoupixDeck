using LoupixDeck.PluginSdk;

namespace LoupixDeck.Commands.Base;

/// <summary>
/// Declares presentation metadata for a core command group (category) shown as a
/// card in the command picker: its section, card icon and short description.
/// Applied at assembly level, once per group. Groups without a declaration fall
/// back to a generic icon and the <see cref="CommandGroupSection.Plugins"/> section
/// (see <c>IGroupCatalog</c>). Purely cosmetic — never persisted.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class CommandGroupAttribute(
    string group,
    string description = null,
    string icon = null,
    CommandGroupSection section = CommandGroupSection.Core) : Attribute
{
    /// <summary>The group name, matching the <c>[Command(..., group)]</c> it describes.</summary>
    public string Group { get; } = group;

    /// <summary>Short description shown under the category title on the card.</summary>
    public string Description { get; } = description;

    /// <summary>MDI glyph (a single UTF-32 code point as a string) shown on the card.</summary>
    public string Icon { get; } = icon;

    /// <summary>Section the category is filed under.</summary>
    public CommandGroupSection Section { get; } = section;
}