using Avalonia.Media;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One segment of a split CT wheel in the rotary editor: its press command strip and the zone icon
/// in the strip's header. The segment model is edited in place, like the rotary's own slots.
/// </summary>
public sealed class WheelSegmentEditor(CommandSequenceSlot slot, WheelSplitMode mode, int index)
{
    /// <summary>The segment's press command, as an editable chip strip titled with the segment's name.</summary>
    public CommandSequenceSlot Slot { get; } = slot;

    /// <summary>The segment's zone of the icon square, filled.</summary>
    public Geometry ZoneFill { get; } = StreamGeometry.Parse(ZoneFillPath(mode, index));

    /// <summary>The icon square's outline and the mode's dividers.</summary>
    public Geometry ZoneLines { get; } = StreamGeometry.Parse(ZoneLinesPath(mode));

    // The icon is a 24 x 24 view box with the square from (2,2) to (22,22); segment order as in
    // Utils.WheelSplitGeometry.
    private static string ZoneFillPath(WheelSplitMode mode, int index) => mode switch
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
        _ => "M2,2 H22 V22 H2 Z"
    };

    private static string ZoneLinesPath(WheelSplitMode mode) => "M2,2 H22 V22 H2 Z " + mode switch
    {
        WheelSplitMode.Horizontal => "M2,12 H22",
        WheelSplitMode.Vertical => "M12,2 V22",
        WheelSplitMode.Diagonal => "M2,2 L22,22 M22,2 L2,22",
        WheelSplitMode.Cross => "M2,12 H22 M12,2 V22",
        _ => string.Empty
    };

    /// <summary>
    /// The order the segment strips are laid out in, two per row: the diagonal split shows top and
    /// right above left and bottom; every other split keeps its segment order.
    /// </summary>
    public static IReadOnlyList<int> DisplayOrder(WheelSplitMode mode, int count) =>
        mode == WheelSplitMode.Diagonal && count == 4 ? [0, 1, 3, 2] : Enumerable.Range(0, count).ToArray();
}

/// <summary>One entry of the split-mode choice in the rotary editor and the wheel appearance editor.</summary>
public sealed record WheelSplitOption(WheelSplitMode Mode, string Label)
{
    public override string ToString() => Label;

    /// <summary>Every split mode, labelled in the UI language.</summary>
    public static IReadOnlyList<WheelSplitOption> All() =>
    [
        new(WheelSplitMode.None, Localization.Loc.Tr("Wheel_SplitNone")),
        new(WheelSplitMode.Horizontal, Localization.Loc.Tr("Wheel_SplitHorizontal")),
        new(WheelSplitMode.Vertical, Localization.Loc.Tr("Wheel_SplitVertical")),
        new(WheelSplitMode.Diagonal, Localization.Loc.Tr("Wheel_SplitDiagonal")),
        new(WheelSplitMode.Cross, Localization.Loc.Tr("Wheel_SplitCross"))
    ];
}
