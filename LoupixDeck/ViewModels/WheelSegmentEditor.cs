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
    public Geometry ZoneFill { get; } = WheelZoneIcons.Fill(mode, index);

    /// <summary>The icon square's outline and the mode's dividers.</summary>
    public Geometry ZoneLines { get; } = WheelZoneIcons.Lines(mode);

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

    /// <summary>The split drawn as a small icon: the wheel square and its dividers.</summary>
    public Geometry Icon { get; } = WheelZoneIcons.Lines(Mode);

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
