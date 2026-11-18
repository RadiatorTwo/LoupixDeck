using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

/// <summary>
/// The LED buttons a command can address: the round buttons, counted from 0 left to right, and
/// the Loupedeck CT's square keys. Each button's value equals its
/// <see cref="Constants.ButtonType"/>, so a cast maps one onto the other; <see cref="All"/> stands
/// for every LED button. The editor offers only the buttons the device has, labelled as printed.
/// The names are persisted in command strings and must not be renamed.
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
    Home = (int)Constants.ButtonType.CT_HOME,
    Undo = (int)Constants.ButtonType.CT_UNDO,
    Keyboard = (int)Constants.ButtonType.CT_KEYBOARD,
    Enter = (int)Constants.ButtonType.CT_ENTER,
    Save = (int)Constants.ButtonType.CT_SAVE,
    FnLeft = (int)Constants.ButtonType.CT_FN_L,
    A = (int)Constants.ButtonType.CT_A,
    B = (int)Constants.ButtonType.CT_B,
    C = (int)Constants.ButtonType.CT_C,
    D = (int)Constants.ButtonType.CT_D,
    FnRight = (int)Constants.ButtonType.CT_FN_R,
    E = (int)Constants.ButtonType.CT_E,
    All = -1
}
