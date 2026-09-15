using LoupixDeck.Utils;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Translates Loupedeck key names to the names <see cref="KeyNames"/> understands. Loupedeck writes
/// shortcuts with the browser's <c>KeyboardEvent.code</c> names plus its own cross-platform modifiers,
/// e.g. <c>ControlOrCommand+Shift+KeyT</c> or <c>AltOrOption+ArrowLeft</c>.
/// </summary>
public static class Lp5KeyNames
{
    private static readonly Dictionary<string, string> Renames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ControlOrCommand"] = "Ctrl",
        ["ControlLeft"] = "Ctrl",
        ["ControlRight"] = "RCtrl",
        ["AltOrOption"] = "Alt",
        ["Option"] = "Alt",
        ["AltLeft"] = "Alt",
        ["AltRight"] = "AltGr",
        ["ShiftLeft"] = "Shift",
        ["ShiftRight"] = "RShift",
        ["MetaLeft"] = "Win",
        ["MetaRight"] = "Win",
        ["OS"] = "Win",
        ["OSLeft"] = "Win",
        ["OSRight"] = "Win",
        ["ContextMenu"] = "Menu",
        ["Backquote"] = "Grave",
        ["Equal"] = "Equals",
        ["BracketLeft"] = "LeftBracket",
        ["BracketRight"] = "RightBracket",
        ["IntlBackslash"] = "Oem102",
        ["NumpadComma"] = "NumDecimal",
        ["MediaTrackNext"] = "NextTrack",
        ["MediaTrackPrevious"] = "PrevTrack",
        ["ResetVolume"] = "Mute"
    };

    /// <summary>
    /// Translates a whole combination (<c>ControlOrCommand+KeyT</c>) to <c>Ctrl+T</c>. Returns null
    /// when any key has no LoupixDeck equivalent, so a shortcut is never sent with a key missing.
    /// </summary>
    public static string TranslateCombination(string combination)
    {
        List<string> parts = KeyNames.SplitCombination(combination);
        if (parts.Count == 0)
            return null;

        List<string> translated = new(parts.Count);
        foreach (string part in parts)
        {
            string key = TranslateKey(part);
            if (key == null)
                return null;

            translated.Add(key);
        }

        return string.Join("+", translated);
    }

    /// <summary>Translates a single key name; null when LoupixDeck cannot send it.</summary>
    public static string TranslateKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string key = name.Trim();

        if (Renames.TryGetValue(key, out string renamed))
            key = renamed;
        else if (key.Length == 4 && key.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetter(key[3]))
            key = key[3..].ToUpperInvariant();
        else if (key.Length == 6 && key.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(key[5]))
            key = key[5..];

        return IsSendable(key) ? key : null;
    }

    private static bool IsSendable(string key) =>
        KeyNames.TryGetWindows(key, out _, out _) || KeyNames.TryGetLinux(key, out _) ||
        KeyNames.TryGetCharacter(key, out _);
}
