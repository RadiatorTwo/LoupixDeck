using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

/// <summary>
/// The round LED buttons a command can address, counted from 1, left to right, like on the virtual
/// device. Each value equals its <see cref="Constants.ButtonType"/>, so a cast maps
/// one onto the other. The names are persisted in command strings and must not be renamed.
/// </summary>
public enum LedButton
{
    Button1 = (int)Constants.ButtonType.BUTTON0,
    Button2 = (int)Constants.ButtonType.BUTTON1,
    Button3 = (int)Constants.ButtonType.BUTTON2,
    Button4 = (int)Constants.ButtonType.BUTTON3,
    Button5 = (int)Constants.ButtonType.BUTTON4,
    Button6 = (int)Constants.ButtonType.BUTTON5,
    Button7 = (int)Constants.ButtonType.BUTTON6,
    Button8 = (int)Constants.ButtonType.BUTTON7
}
