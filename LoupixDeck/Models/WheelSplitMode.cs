namespace LoupixDeck.Models;

/// <summary>
/// How a Loupedeck CT wheel mode divides its round wheel screen into separately pressable
/// segments, each with its own press command and canvas (PluginSdk issue #30). Persisted per
/// wheel mode as its number, so the values never change; the members mirror the SDK's
/// <see cref="LoupixDeck.PluginSdk.WheelSplitMode"/>, which a plugin sees. The segment order is
/// documented in <see cref="Utils.WheelSplitGeometry"/>.
/// </summary>
public enum WheelSplitMode
{
    /// <summary>Default: the whole wheel screen is one button.</summary>
    None = 0,

    /// <summary>Two halves split by a horizontal line: top, bottom.</summary>
    Horizontal = 1,

    /// <summary>Two halves split by a vertical line: left, right.</summary>
    Vertical = 2,

    /// <summary>Four triangles split by both diagonals (an X): top, right, bottom, left.</summary>
    Diagonal = 3,

    /// <summary>Four quadrants split like a +: top-left, top-right, bottom-left, bottom-right.</summary>
    Cross = 4
}
