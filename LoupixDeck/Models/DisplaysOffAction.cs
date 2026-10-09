namespace LoupixDeck.Models;

/// <summary>
/// What a device that follows the host's monitors does while they are off (issue #382).
/// </summary>
public enum DisplaysOffAction
{
    /// <summary>Blank the display and the LEDs, like the manual device off.</summary>
    TurnOff,

    /// <summary>Lower the display brightness and keep everything else running.</summary>
    Dim
}
