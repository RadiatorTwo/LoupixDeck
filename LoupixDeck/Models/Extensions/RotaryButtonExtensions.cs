using LoupixDeck.Localization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Models.Extensions;

/// <summary>
/// Addresses a dial's three gesture slots by <see cref="RotaryAction"/>. The slots are three
/// unrelated properties on the model — two of its own plus the inherited press command — so without
/// this every caller that works with a gesture repeats the same switch.
/// </summary>
public static class RotaryButtonExtensions
{
    /// <summary>The command bound to <paramref name="action"/>, or an empty chain when the gesture
    /// is unassigned.</summary>
    public static CommandBinding GetCommand(this RotaryButton dial, RotaryAction action) => action switch
    {
        RotaryAction.CounterClockwise => dial?.RotaryLeftCommand ?? CommandBinding.Empty,
        RotaryAction.Clockwise => dial?.RotaryRightCommand ?? CommandBinding.Empty,
        RotaryAction.Press => dial?.Command ?? CommandBinding.Empty,
        _ => CommandBinding.Empty
    };

    /// <summary>Binds <paramref name="command"/> to <paramref name="action"/>. An empty command
    /// clears the gesture.</summary>
    public static void SetCommand(this RotaryButton dial, RotaryAction action, CommandBinding command)
    {
        if (dial == null)
            return;

        switch (action)
        {
            case RotaryAction.CounterClockwise:
                dial.RotaryLeftCommand = command;
                break;

            case RotaryAction.Clockwise:
                dial.RotaryRightCommand = command;
                break;

            case RotaryAction.Press:
                dial.Command = command;
                break;
        }
    }

    /// <summary>True when no gesture of this dial is bound to anything.</summary>
    public static bool IsEmpty(this RotaryButton dial) =>
        CommandBinding.IsNullOrEmpty(dial?.RotaryLeftCommand) &&
        CommandBinding.IsNullOrEmpty(dial?.RotaryRightCommand) &&
        CommandBinding.IsNullOrEmpty(dial?.Command);

    /// <summary>The gestures in the order they are shown to the user.</summary>
    public static IReadOnlyList<RotaryAction> Gestures { get; } =
        [RotaryAction.CounterClockwise, RotaryAction.Clockwise, RotaryAction.Press];

    /// <summary>The user-facing name of a gesture.</summary>
    public static string DisplayName(this RotaryAction action) => action switch
    {
        RotaryAction.CounterClockwise => Loc.Tr("Gesture_RotateLeft"),
        RotaryAction.Clockwise => Loc.Tr("Gesture_RotateRight"),
        RotaryAction.Press => Loc.Tr("Gesture_Press"),
        _ => action.ToString()
    };
}
