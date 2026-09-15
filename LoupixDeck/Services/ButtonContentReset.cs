using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// Empties a button's user configuration while keeping the instance, so subscriptions on it
/// survive. Shared by the editor's Clear and by code that removes content elsewhere.
/// </summary>
public static class ButtonContentReset
{
    // Collapse a touch/strip button to a single empty default state (keeps the instance, so its
    // ItemChanged subscription survives), then repaint.
    public static void ClearTouchContent(TouchButton button)
    {
        while (button.States.Count > 1)
            button.States.RemoveAt(button.States.Count - 1);

        ButtonState state = button.States[0];
        state.Layers.Clear();
        state.BackColor = Avalonia.Media.Colors.Black;
        state.BackgroundEnabled = false;
        state.LedColor = Avalonia.Media.Colors.Black;
        state.Command = null;
        state.VibrationEnabled = false;
        state.Transition.Kind = StateTransitionKind.Stay;
        state.Transition.TargetStateId = null;

        button.DefaultStateId = state.Id;
        button.Command = null;
        ReleaseStateOwnership(button);
        button.SetActiveState(state.Id);
    }

    public static void ClearSimpleContent(SimpleButton button)
    {
        while (button.States.Count > 1)
            button.States.RemoveAt(button.States.Count - 1);

        ButtonState state = button.States[0];
        state.LedColor = Avalonia.Media.Colors.Black;
        state.Command = null;
        state.Transition.Kind = StateTransitionKind.Stay;
        state.Transition.TargetStateId = null;

        button.DefaultStateId = state.Id;
        button.Command = null;
        ReleaseStateOwnership(button);
        button.SetActiveState(state.Id);
    }

    public static void ClearRotaryContent(RotaryButton button)
    {
        button.Command = null;
        button.RotaryLeftCommand = string.Empty;
        button.RotaryRightCommand = string.Empty;
        button.DisplayText = string.Empty;
        button.Refresh();
    }

    /// <summary>
    /// Hands a cleared button's states back to the user. Without this the button would still
    /// claim a command owns its states, and assigning one that declares states would ask whether
    /// to keep states that are no longer there.
    /// </summary>
    private static void ReleaseStateOwnership(StatefulButton button)
    {
        button.StateOwnerCommand = null;
        button.Mode = ButtonStateMode.Local;
        button.ResetOnPageChange = false;
    }
}
