namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Translates Loupedeck key-combination strings (WPF key names, e.g. <c>ControlOrCommand+KeyS</c>)
/// into the key names <c>System.KeyCombination</c> understands (<c>Ctrl+S</c>).
/// </summary>
internal static class Lp5KeyCombo
{
    private static readonly Dictionary<string, string> KeyMap = new(StringComparer.Ordinal)
    {
        ["ControlOrCommand"] = "Ctrl",
        ["Control"] = "Ctrl",
        ["AltOrOption"] = "Alt",
        ["Windows"] = "Win",
        ["ArrowUp"] = "Up",
        ["ArrowDown"] = "Down",
        ["ArrowLeft"] = "Left",
        ["ArrowRight"] = "Right",
        ["Back"] = "Backspace",
        ["Delete"] = "Del",
        ["Add"] = "NumPlus",
        ["Subtract"] = "NumMinus",
        // WPF OEM names -> the physical US-position names LoupixDeck uses.
        ["Oem1"] = "Semicolon",
        ["Oem2"] = "Slash",
        ["Oem4"] = "LeftBracket",
        ["Oem5"] = "Backslash",
        ["Oem6"] = "RightBracket",
        ["Oem7"] = "Quote"
    };

    /// <summary>The normalized combination, or null when the input holds no key.</summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        // Loupedeck appends "___<suffix>" metadata to some key strings.
        int suffix = raw.IndexOf("___", StringComparison.Ordinal);
        string combo = (suffix >= 0 ? raw[..suffix] : raw).Trim();
        if (combo.Length == 0) return null;

        List<string> keys = [];
        foreach (string part in combo.Split('+'))
        {
            string token = part.Trim();
            if (token.Length == 0)
            {
                // A literal '+' key shows up as empty tokens; keep it.
                keys.Add("+");
                continue;
            }

            if (token.StartsWith("Key", StringComparison.Ordinal) && token.Length > 3)
                token = token[3..];

            keys.Add(KeyMap.GetValueOrDefault(token, token));
        }

        return string.Join('+', keys);
    }
}
