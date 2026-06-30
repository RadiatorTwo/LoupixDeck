namespace LoupixDeck.Models;

/// <summary>
/// How a stateful button's active state is advanced.
/// </summary>
public enum ButtonStateMode
{
    /// <summary>Each press runs the active state's command, then applies its configured transition.</summary>
    Local,

    /// <summary>The active state is driven by a plugin; presses never change the state automatically.</summary>
    External
}
