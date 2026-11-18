using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

/// <summary>
/// The round LED buttons a command can address, counted from 0, left to right, as the Loupedeck
/// Live, Live S and Razer Stream Controller print them. Each button's value equals its
/// <see cref="Constants.ButtonType"/>, so a cast maps one onto the other; <see cref="All"/> stands
/// for every LED button. The names are persisted in command strings and must not be renamed.
/// </summary>
public enum LedButton
{
    Button0 = (int)Constants.ButtonType.BUTTON0,
    Button1 = (int)Constants.ButtonType.BUTTON1,
    Button2 = (int)Constants.ButtonType.BUTTON2,
    Button3 = (int)Constants.ButtonType.BUTTON3,
    Button4 = (int)Constants.ButtonType.BUTTON4,
    Button5 = (int)Constants.ButtonType.BUTTON5,
    Button6 = (int)Constants.ButtonType.BUTTON6,
    Button7 = (int)Constants.ButtonType.BUTTON7,
    All = -1
}
