using Avalonia.Input;

namespace LoupixDeck.Utils;

/// <summary>
/// Maps an Avalonia <see cref="Key"/> captured from the UI to the canonical key name
/// used in macro steps (the same names <see cref="KeyNames"/> understands). Only keys
/// that the keyboard backends can actually replay are mapped; everything else is ignored.
/// Names are produced in a display-friendly casing (e.g. "Ctrl", "F5", "PageUp", "S"),
/// which <see cref="KeyNames"/> resolves case-insensitively.
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
    };

    /// <summary>True for keys that are modifiers (used to keep them first in a combo).</summary>
    public static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.Apps;

    /// <summary>Resolves an Avalonia key to its canonical macro key name.</summary>
    public static bool TryGet(Key key, out string name) => Map.TryGetValue(key, out name);
}
