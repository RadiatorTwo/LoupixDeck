namespace LoupixDeck.Models;

/// <summary>
/// The touch buttons of a page a command can address, counted from 1, left to right and top to
/// bottom. Fifteen covers the largest grid (5 × 3); a number the device does not have is ignored.
/// <see cref="All"/> stands for every button of the page. The names are persisted in command
/// strings and must not be renamed.
/// </summary>
public enum TouchButtonNumber
{
    Button1 = 1,
    Button2,
    Button3,
    Button4,
    Button5,
    Button6,
    Button7,
    Button8,
    Button9,
    Button10,
    Button11,
    Button12,
    Button13,
    Button14,
    Button15,
    All = -1
}
