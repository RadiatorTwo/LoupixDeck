using LoupixDeck.PluginSdk;

namespace LoupixDeck.Models.Extensions;

/// <summary>
/// Addresses a dial's three gesture slots by <see cref="RotaryAction"/>. The slots are three
/// unrelated properties on the model — two of its own plus the inherited press command — so without
/// this every caller that works with a gesture repeats the same switch.
/// </summary>
public static class RotaryButtonExtensions
{
    /// <summary>The command bound to <paramref name="action"/>, or an empty string when the gesture
    /// is unassigned.</summary>
    public static string GetCommand(this RotaryButton dial, RotaryAction action) => action switch
    {
        RotaryAction.CounterClockwise => dial?.RotaryLeftCommand ?? string.Empty,
        RotaryAction.Clockwise => dial?.RotaryRightCommand ?? string.Empty,
        RotaryAction.Press => dial?.Command ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>Binds <paramref name="command"/> to <paramref name="action"/>. An empty command
    /// clears the gesture.</summary>
    public static void SetCommand(this RotaryButton dial, RotaryAction action, string command)
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
        string.IsNullOrWhiteSpace(dial?.RotaryLeftCommand) &&
        string.IsNullOrWhiteSpace(dial?.RotaryRightCommand) &&
        string.IsNullOrWhiteSpace(dial?.Command);

    /// <summary>The gestures in the order they are shown to the user.</summary>
    public static IReadOnlyList<RotaryAction> Gestures { get; } =
        [RotaryAction.CounterClockwise, RotaryAction.Clockwise, RotaryAction.Press];

    /// <summary>The user-facing name of a gesture.</summary>
    public static string DisplayName(this RotaryAction action) => action switch
    {
        RotaryAction.CounterClockwise => "Rotate Left",
        RotaryAction.Clockwise => "Rotate Right",
        RotaryAction.Press => "Press",
        _ => action.ToString()
    };
}
