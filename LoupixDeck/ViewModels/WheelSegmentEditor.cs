using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One segment of a split CT wheel in the rotary editor: its press command strip. The segment
/// model is edited in place, like the rotary's own slots.
/// </summary>
public sealed class WheelSegmentEditor(CommandSequenceSlot slot)
{
    /// <summary>The segment's press command, as an editable chip strip titled with the segment's name.</summary>
    public CommandSequenceSlot Slot { get; } = slot;
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
