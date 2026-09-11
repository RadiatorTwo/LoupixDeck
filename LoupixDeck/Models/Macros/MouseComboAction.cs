namespace LoupixDeck.Models.Macros;

/// <summary>
/// The mouse half of a keyboard-plus-mouse chord (<c>System.MouseCombo</c>): either a button to
/// click or a wheel direction to scroll. The member NAME is written into the button's command
/// string, so renaming a member is a breaking change while appending new ones is safe.
/// </summary>
public enum MouseComboAction
{
    Left,
    Right,
    Middle,
    X1,
    X2,
    ScrollUp,
    ScrollDown
}
