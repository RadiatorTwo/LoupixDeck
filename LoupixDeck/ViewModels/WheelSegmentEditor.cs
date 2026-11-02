using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// One segment of a split CT wheel in the rotary editor: its press command strip and its label.
/// The segment model is edited in place, like the rotary's own slots.
/// </summary>
public sealed class WheelSegmentEditor(WheelSegment segment, CommandSequenceSlot slot)
{
    /// <summary>The edited segment; its <see cref="WheelSegment.DisplayText"/> is bound directly.</summary>
    public WheelSegment Segment { get; } = segment;

    /// <summary>The segment's press command, as an editable chip strip titled with the segment's name.</summary>
    public CommandSequenceSlot Slot { get; } = slot;
}

/// <summary>One entry of the split-mode choice in the rotary editor.</summary>
public sealed record WheelSplitOption(WheelSplitMode Mode, string Label)
{
    public override string ToString() => Label;
}
