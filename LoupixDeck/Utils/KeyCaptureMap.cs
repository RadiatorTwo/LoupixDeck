using Avalonia.Input;

namespace LoupixDeck.Utils;

/// <summary>
/// Maps a key press captured from the UI to the canonical key name used in macro steps
/// (the same names <see cref="KeyNames"/> understands). Names are produced in a
/// display-friendly casing (e.g. "Ctrl", "F5", "PageUp", "S"), which
/// <see cref="KeyNames"/> resolves case-insensitively.
///
/// Three sources are consulted in order, see <see cref="TryResolve"/>: the
/// <see cref="Key"/> table below, then the character the key produced, then its physical
/// position. The character step matters for everything outside the alphanumeric block —
/// the ü key reports <see cref="Key.OemSemicolon"/> on a German layout but
/// <see cref="Key.OemOpenBrackets"/> on a US one, so the <see cref="Key"/> value alone
/// cannot name a punctuation key; the character it produced can.
/// </summary>
public static class KeyCaptureMap
{
    private static readonly Dictionary<Key, string> Map = new()
    {
        // Modifiers
        [Key.LeftCtrl] = "Ctrl",
        [Key.RightCtrl] = "RCtrl",
        [Key.LeftShift] = "Shift",
        [Key.RightShift] = "RShift",
        [Key.LeftAlt] = "Alt",
        [Key.RightAlt] = "AltGr",
        [Key.LWin] = "Win",
        [Key.RWin] = "Win",
        [Key.Apps] = "Menu",

        // Whitespace / control keys
        [Key.Space] = "Space",
        [Key.Return] = "Enter",
        [Key.Tab] = "Tab",
        [Key.Escape] = "Esc",
        [Key.Back] = "Backspace",
        [Key.CapsLock] = "CapsLock",

        // Navigation block
        [Key.Insert] = "Ins",
        [Key.Delete] = "Del",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "PageUp",
        [Key.PageDown] = "PageDown",
        [Key.Up] = "Up",
        [Key.Down] = "Down",
        [Key.Left] = "Left",
        [Key.Right] = "Right",

        // Function keys
        [Key.F1] = "F1", [Key.F2] = "F2", [Key.F3] = "F3", [Key.F4] = "F4",
        [Key.F5] = "F5", [Key.F6] = "F6", [Key.F7] = "F7", [Key.F8] = "F8",
        [Key.F9] = "F9", [Key.F10] = "F10", [Key.F11] = "F11", [Key.F12] = "F12",

        // Letters
        [Key.A] = "A", [Key.B] = "B", [Key.C] = "C", [Key.D] = "D", [Key.E] = "E",
        [Key.F] = "F", [Key.G] = "G", [Key.H] = "H", [Key.I] = "I", [Key.J] = "J",
        [Key.K] = "K", [Key.L] = "L", [Key.M] = "M", [Key.N] = "N", [Key.O] = "O",
        [Key.P] = "P", [Key.Q] = "Q", [Key.R] = "R", [Key.S] = "S", [Key.T] = "T",
        [Key.U] = "U", [Key.V] = "V", [Key.W] = "W", [Key.X] = "X", [Key.Y] = "Y",
        [Key.Z] = "Z",

        // Digits (number row)
        [Key.D0] = "0", [Key.D1] = "1", [Key.D2] = "2", [Key.D3] = "3", [Key.D4] = "4",
        [Key.D5] = "5", [Key.D6] = "6", [Key.D7] = "7", [Key.D8] = "8", [Key.D9] = "9",

        // Media and volume keys. Their virtual keys do not depend on the layout, so unlike
        // the punctuation keys they can be named from the Key value alone. Windows delivers
        // them as WM_APPCOMMAND rather than a key message, so a capture may never see them —
        // the names can always be typed into the field instead.
        [Key.MediaPlayPause] = "PlayPause",
        [Key.MediaNextTrack] = "NextTrack",
        [Key.MediaPreviousTrack] = "PrevTrack",
        [Key.MediaStop] = "MediaStop",
        [Key.VolumeMute] = "Mute",
        [Key.VolumeDown] = "VolumeDown",
        [Key.VolumeUp] = "VolumeUp",

        // Browser and launcher keys
        [Key.BrowserBack] = "BrowserBack",
        [Key.BrowserForward] = "BrowserForward",
        [Key.BrowserRefresh] = "BrowserRefresh",
        [Key.BrowserStop] = "BrowserStop",
        [Key.BrowserSearch] = "BrowserSearch",
        [Key.BrowserFavorites] = "BrowserFavorites",
        [Key.BrowserHome] = "BrowserHome",
        [Key.LaunchMail] = "LaunchMail",
        [Key.SelectMedia] = "LaunchMedia",
        [Key.LaunchApplication1] = "LaunchComputer",
        [Key.LaunchApplication2] = "LaunchCalculator",
    };

    // Physical position -> canonical position name. Used for keys that produce no character
    // (dead keys such as ^ and ´ on a German board, the keypad, F13+, PrintScreen) and as the
    // last resort for punctuation. Positions are layout-neutral, so this table is correct on
    // every layout — unlike the Key values, which are not.
    private static readonly Dictionary<PhysicalKey, string> Physical = new()
    {
        // Punctuation / OEM row
        [PhysicalKey.Backquote] = "Grave",
        [PhysicalKey.Minus] = "Minus",
        [PhysicalKey.Equal] = "Equals",
        [PhysicalKey.BracketLeft] = "LeftBracket",
        [PhysicalKey.BracketRight] = "RightBracket",
        [PhysicalKey.Semicolon] = "Semicolon",
        [PhysicalKey.Quote] = "Quote",
        [PhysicalKey.Backslash] = "Backslash",
        [PhysicalKey.Comma] = "Comma",
        [PhysicalKey.Period] = "Period",
        [PhysicalKey.Slash] = "Slash",
        [PhysicalKey.IntlBackslash] = "Oem102",

        // Numeric keypad
        [PhysicalKey.NumLock] = "NumLock",
        [PhysicalKey.NumPad0] = "Num0", [PhysicalKey.NumPad1] = "Num1",
        [PhysicalKey.NumPad2] = "Num2", [PhysicalKey.NumPad3] = "Num3",
        [PhysicalKey.NumPad4] = "Num4", [PhysicalKey.NumPad5] = "Num5",
        [PhysicalKey.NumPad6] = "Num6", [PhysicalKey.NumPad7] = "Num7",
        [PhysicalKey.NumPad8] = "Num8", [PhysicalKey.NumPad9] = "Num9",
        [PhysicalKey.NumPadDivide] = "NumDivide",
        [PhysicalKey.NumPadMultiply] = "NumMultiply",
        [PhysicalKey.NumPadSubtract] = "NumMinus",
        [PhysicalKey.NumPadAdd] = "NumPlus",
        [PhysicalKey.NumPadEnter] = "NumEnter",
        [PhysicalKey.NumPadDecimal] = "NumDecimal",

        // System keys
        [PhysicalKey.PrintScreen] = "PrintScreen",
        [PhysicalKey.ScrollLock] = "ScrollLock",
        [PhysicalKey.Pause] = "Pause",

        // Media and volume keys
        [PhysicalKey.MediaPlayPause] = "PlayPause",
        [PhysicalKey.MediaTrackNext] = "NextTrack",
        [PhysicalKey.MediaTrackPrevious] = "PrevTrack",
        [PhysicalKey.MediaStop] = "MediaStop",
        [PhysicalKey.AudioVolumeMute] = "Mute",
        [PhysicalKey.AudioVolumeDown] = "VolumeDown",
        [PhysicalKey.AudioVolumeUp] = "VolumeUp",

        // Browser and launcher keys
        [PhysicalKey.BrowserBack] = "BrowserBack",
        [PhysicalKey.BrowserForward] = "BrowserForward",
        [PhysicalKey.BrowserRefresh] = "BrowserRefresh",
        [PhysicalKey.BrowserStop] = "BrowserStop",
        [PhysicalKey.BrowserSearch] = "BrowserSearch",
        [PhysicalKey.BrowserFavorites] = "BrowserFavorites",
        [PhysicalKey.BrowserHome] = "BrowserHome",
        [PhysicalKey.LaunchMail] = "LaunchMail",
        [PhysicalKey.MediaSelect] = "LaunchMedia",
        [PhysicalKey.LaunchApp1] = "LaunchComputer",
        [PhysicalKey.LaunchApp2] = "LaunchCalculator",

        // Extended function keys
        [PhysicalKey.F13] = "F13", [PhysicalKey.F14] = "F14", [PhysicalKey.F15] = "F15",
        [PhysicalKey.F16] = "F16", [PhysicalKey.F17] = "F17", [PhysicalKey.F18] = "F18",
        [PhysicalKey.F19] = "F19", [PhysicalKey.F20] = "F20", [PhysicalKey.F21] = "F21",
        [PhysicalKey.F22] = "F22", [PhysicalKey.F23] = "F23", [PhysicalKey.F24] = "F24",
    };

    /// <summary>True for keys that are modifiers (used to keep them first in a combo).</summary>
    public static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.Apps;

    /// <summary>
    /// Resolves a captured key press to its canonical macro key name, trying the named-key
    /// table, then the character the press produced, then the physical position.
    /// </summary>
    /// <param name="key">The Avalonia key, layout-mapped.</param>
    /// <param name="physicalKey">The physical position of the key, layout-neutral.</param>
    /// <param name="keySymbol">The character the press produced, if any.</param>
    public static bool TryResolve(Key key, PhysicalKey physicalKey, string keySymbol, out string name)
    {
        if (Map.TryGetValue(key, out name))
            return true;

        // A single printable character names the key by what it types ("ü", "#", "+"), which
        // is both readable and layout-correct: the backends resolve it through the layout.
        if (keySymbol?.Length == 1 && !char.IsControl(keySymbol[0]) && !char.IsWhiteSpace(keySymbol[0]))
        {
            name = keySymbol;
            return true;
        }

        return Physical.TryGetValue(physicalKey, out name);
    }
}
