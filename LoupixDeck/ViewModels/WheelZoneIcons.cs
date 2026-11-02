using Avalonia.Media;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Small zone icons of a split CT wheel, shared by the rotary editor's strip headers and the wheel
/// appearance editor: a 24 x 24 view box with the wheel drawn as the square from (2,2) to (22,22),
/// the split's dividers as lines and one zone filled. Segment order as in
/// <see cref="Utils.WheelSplitGeometry"/>.
/// </summary>
public static class WheelZoneIcons
{
    private const string Square = "M2,2 H22 V22 H2 Z";

    /// <summary>The whole square, filled: the unsplit wheel, or all segments together.</summary>
    public static Geometry Whole() => StreamGeometry.Parse(Square);

    /// <summary>Segment <paramref name="index"/> of <paramref name="mode"/>, filled.</summary>
    public static Geometry Fill(WheelSplitMode mode, int index) => StreamGeometry.Parse(mode switch
    {
        WheelSplitMode.Horizontal => index == 0 ? "M2,2 H22 V12 H2 Z" : "M2,12 H22 V22 H2 Z",
        WheelSplitMode.Vertical => index == 0 ? "M2,2 H12 V22 H2 Z" : "M12,2 H22 V22 H12 Z",
        WheelSplitMode.Diagonal => index switch
        {
            0 => "M2,2 L22,2 L12,12 Z",
            1 => "M22,2 L22,22 L12,12 Z",
            2 => "M2,22 L22,22 L12,12 Z",
            _ => "M2,2 L2,22 L12,12 Z"
        },
        WheelSplitMode.Cross => index switch
        {
            0 => "M2,2 H12 V12 H2 Z",
            1 => "M12,2 H22 V12 H12 Z",
            2 => "M2,12 H12 V22 H2 Z",
            _ => "M12,12 H22 V22 H12 Z"
        },
        _ => Square
    });

    /// <summary>Only the dividers of <paramref name="mode"/>, without the square; empty when unsplit.</summary>
    public static Geometry Dividers(WheelSplitMode mode) => StreamGeometry.Parse(mode switch
    {
        WheelSplitMode.Horizontal => "M2,12 H22",
        WheelSplitMode.Vertical => "M12,2 V22",
        WheelSplitMode.Diagonal => "M2,2 L22,22 M22,2 L2,22",
        WheelSplitMode.Cross => "M2,12 H22 M12,2 V22",
        _ => "M0,0"
    });

    /// <summary>The square's outline and the dividers of <paramref name="mode"/>.</summary>
    public static Geometry Lines(WheelSplitMode mode) => StreamGeometry.Parse(Square + mode switch
    {
        WheelSplitMode.Horizontal => " M2,12 H22",
        WheelSplitMode.Vertical => " M12,2 V22",
        WheelSplitMode.Diagonal => " M2,2 L22,22 M22,2 L2,22",
        WheelSplitMode.Cross => " M2,12 H22 M12,2 V22",
        _ => string.Empty
    });
}

/// <summary>
/// One surface of a split wheel in the appearance editor's tab bar: all segments together, or one
/// segment, with its zone icon.
/// </summary>
public sealed record WheelSurfaceChoice(string Label, Geometry Fill, Geometry Lines);
