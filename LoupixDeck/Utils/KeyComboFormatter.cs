using Avalonia.Input;

namespace LoupixDeck.Utils;

/// <summary>
/// Turns pressed <see cref="Key"/> values into the token strings the keyboard commands expect
/// (<c>"Ctrl+Shift+S"</c>). Kept apart from the capture dialog so the mapping is testable and has
/// a single definition.
/// </summary>
public static class KeyComboFormatter
{
    /// <summary>True for a key that is only a modifier and never a combination on its own.</summary>
    public static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>
    /// Builds the combination string from the keys in the exact order they were pressed. Returns
    /// null while only modifiers are held unless <paramref name="modifiersOnly"/> allows it — a
    /// dangling "Ctrl+" is not a combination the commands can run.
    /// </summary>
    public static string BuildCombo(IReadOnlyList<Key> keys, bool modifiersOnly = false)
    {
        List<string> parts = [];
        bool hasKey = false;

        foreach (Key key in keys)
        {
            string token = TokenFor(key);
            if (token == null)
                continue;

            parts.Add(token);
            if (!IsModifier(key))
                hasKey = true;
        }

        if (parts.Count == 0)
            return null;

        return (hasKey || modifiersOnly) ? string.Join("+", parts) : null;
    }

    /// <summary>What to show mid-capture, before a non-modifier key has landed.</summary>
    public static string PartialText(IReadOnlyList<Key> keys)
    {
        string joined = string.Join("+", keys.Select(TokenFor).Where(token => token != null));
        return string.IsNullOrEmpty(joined) ? string.Empty : joined + "+…";
    }

    /// <summary>The token for a key: a canonical modifier name, or the key's own name.</summary>
    public static string TokenFor(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin or Key.RWin => "Win",

        // Avalonia reports Alt as Key.System while it is acting as a menu accelerator; the real
        // Alt token is already produced by LeftAlt/RightAlt, so drop it.
        Key.System => null,

        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),

        Key.Back => "Backspace",
        _ => key.ToString()
    };
}
