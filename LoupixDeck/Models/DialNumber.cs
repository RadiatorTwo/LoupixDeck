namespace LoupixDeck.Models;

/// <summary>
/// The dials of a rotary page a command can address, counted from 1 (top to bottom on the Live S).
/// <see cref="All"/> stands for every dial of the page. The names are persisted in command strings
/// and must not be renamed.
/// </summary>
public enum DialNumber
{
    Dial1 = 1,
    Dial2,
    All = -1
}
