using LoupixDeck.PluginSdk;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// A named set of three rotary commands — one per dial gesture — that can be loaded onto any dial
/// in one step. Built-in presets are declared in code; the user's own live in
/// <c>dial-presets.json</c> next to the macros, so a preset made on one deck works on every other.
/// </summary>
/// <remarks>
/// Applying a preset is a one-shot copy of three command strings: the dial keeps no reference to
/// the preset it came from. Renaming or deleting a preset therefore never changes a dial that was
/// configured with it. The <see cref="Id"/> exists so the editor can address a preset without going
/// through its name, which is neither unique nor stable.
/// </remarks>
public sealed class DialPreset
{
    /// <summary>mdi-cog — the glyph a user-created preset is shown with.</summary>
    public const string DefaultGlyph = "\U000F0493";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Command run when the dial is turned counter-clockwise. Empty leaves the gesture
    /// unassigned.</summary>
    public string Left { get; set; } = string.Empty;

    /// <summary>Command run when the dial is turned clockwise.</summary>
    public string Right { get; set; } = string.Empty;

    /// <summary>Command run when the dial is pressed.</summary>
    public string Press { get; set; } = string.Empty;

    /// <summary>True for a preset that ships with the app: it can be applied but not edited or
    /// removed. Never persisted — the built-ins are code, not file content.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    /// <summary>MDI glyph for the panel row and the menu entry. Built-ins declare their own; a
    /// user-created preset uses <see cref="DefaultGlyph"/>.</summary>
    [JsonIgnore]
    public string Glyph { get; init; } = DefaultGlyph;

    /// <summary>True when the preset would leave every gesture unassigned, which is never worth
    /// offering.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Left) &&
        string.IsNullOrWhiteSpace(Right) &&
        string.IsNullOrWhiteSpace(Press);

    /// <summary>
    /// The preset as the rotary-group map the rest of the app already speaks — the same shape a
    /// plugin's <see cref="MenuNode.RotaryGroup"/> carries, so a preset can be applied by the code
    /// that already applies those. Gestures the preset leaves empty are omitted, which means they
    /// keep whatever the dial had.
    /// </summary>
    public IReadOnlyDictionary<RotaryAction, string> ToRotaryGroup()
    {
        Dictionary<RotaryAction, string> map = [];

        if (!string.IsNullOrWhiteSpace(Left))
            map[RotaryAction.CounterClockwise] = Left;

        if (!string.IsNullOrWhiteSpace(Right))
            map[RotaryAction.Clockwise] = Right;

        if (!string.IsNullOrWhiteSpace(Press))
            map[RotaryAction.Press] = Press;

        return map;
    }

    /// <summary>An independent copy, for editing a preset without touching the stored one.</summary>
    public DialPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Left = Left,
        Right = Right,
        Press = Press,
        IsBuiltIn = IsBuiltIn,
        Glyph = Glyph
    };
}
