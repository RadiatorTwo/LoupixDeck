namespace LoupixDeck.Utils;

/// <summary>
/// Maps human-readable key names (e.g. "Ctrl", "Alt", "F4", "Up") used in key-combination
/// macros to the platform-specific codes the keyboard backends expect.
///
/// - Linux: Linux input-event (evdev) key codes, written to /dev/uinput.
/// - Windows: virtual-key codes (VK_*) plus an "extended key" flag, sent via SendInput.
/// - Interception: PS/2 set-1 scan codes plus an "E0 extended" flag, sent via interception.dll.
///
/// Names are matched case-insensitively and a few common aliases are accepted
/// ("Control"->Ctrl, "Escape"->Esc, "Windows"/"Super"->Win, ...).
///
/// Two naming families exist side by side:
///
/// - <b>Position names</b> (this table) identify a key by its physical place on the board,
///   using the US legend as the naming basis: "Semicolon", "LeftBracket", "Minus", "Oem102",
///   "Num5", "F13". They are layout-neutral, so a macro keeps hitting the same physical key
///   on any layout. The "Oem*" aliases name the same positions the way Windows does.
/// - <b>Character names</b> ("ü", "ä", "ß", "#", "+") are resolved by the backends against
///   the active keyboard layout, not by this table — see the char paths in the keyboard
///   backends. That is what makes umlauts and punctuation work at all: their physical
///   position differs per layout, so only the layout can answer where they live.
///
/// Both spellings resolve, so a config may mix them and older configs keep working.
/// </summary>
public static class KeyNames
{
    // Canonical name -> Linux evdev key code (see input-event-codes.h).
    private static readonly Dictionary<string, int> Linux = new(StringComparer.OrdinalIgnoreCase)
    {
        // Modifiers
        ["ctrl"] = 29,        // KEY_LEFTCTRL
        ["rctrl"] = 97,       // KEY_RIGHTCTRL
        ["shift"] = 42,       // KEY_LEFTSHIFT
        ["rshift"] = 54,      // KEY_RIGHTSHIFT
        ["alt"] = 56,         // KEY_LEFTALT
        ["altgr"] = 100,      // KEY_RIGHTALT
        ["win"] = 125,        // KEY_LEFTMETA
        ["menu"] = 127,       // KEY_COMPOSE (context menu / apps key)

        // Whitespace / control keys
        ["space"] = 57,       // KEY_SPACE
        ["enter"] = 28,       // KEY_ENTER
        ["tab"] = 15,         // KEY_TAB
        ["esc"] = 1,          // KEY_ESC
        ["backspace"] = 14,   // KEY_BACKSPACE
        ["capslock"] = 58,    // KEY_CAPSLOCK

        // Navigation block
        ["ins"] = 110,        // KEY_INSERT
        ["del"] = 111,        // KEY_DELETE
        ["home"] = 102,       // KEY_HOME
        ["end"] = 107,        // KEY_END
        ["pageup"] = 104,     // KEY_PAGEUP
        ["pagedown"] = 109,   // KEY_PAGEDOWN
        ["up"] = 103,         // KEY_UP
        ["down"] = 108,       // KEY_DOWN
        ["left"] = 105,       // KEY_LEFT
        ["right"] = 106,      // KEY_RIGHT

        // Function keys
        ["f1"] = 59, ["f2"] = 60, ["f3"] = 61, ["f4"] = 62, ["f5"] = 63, ["f6"] = 64,
        ["f7"] = 65, ["f8"] = 66, ["f9"] = 67, ["f10"] = 68, ["f11"] = 87, ["f12"] = 88,

        // Letters
        ["a"] = 30, ["b"] = 48, ["c"] = 46, ["d"] = 32, ["e"] = 18, ["f"] = 33, ["g"] = 34,
        ["h"] = 35, ["i"] = 23, ["j"] = 36, ["k"] = 37, ["l"] = 38, ["m"] = 50, ["n"] = 49,
        ["o"] = 24, ["p"] = 25, ["q"] = 16, ["r"] = 19, ["s"] = 31, ["t"] = 20, ["u"] = 22,
        ["v"] = 47, ["w"] = 17, ["x"] = 45, ["y"] = 21, ["z"] = 44,

        // Digits (number row)
        ["0"] = 11, ["1"] = 2, ["2"] = 3, ["3"] = 4, ["4"] = 5, ["5"] = 6, ["6"] = 7,
        ["7"] = 8, ["8"] = 9, ["9"] = 10,

        // Punctuation / OEM keys, named by their US-layout position. On a German board these
        // are the umlaut and sign keys (LeftBracket = ü, Semicolon = ö, Quote = ä, Minus = ß).
        ["grave"] = 41,        // KEY_GRAVE        (US `~   / DE ^°)
        ["minus"] = 12,        // KEY_MINUS        (US -_   / DE ß?)
        ["equals"] = 13,       // KEY_EQUAL        (US =+   / DE ´`)
        ["leftbracket"] = 26,  // KEY_LEFTBRACE    (US [{   / DE ü)
        ["rightbracket"] = 27, // KEY_RIGHTBRACE   (US ]}   / DE +*)
        ["semicolon"] = 39,    // KEY_SEMICOLON    (US ;:   / DE ö)
        ["quote"] = 40,        // KEY_APOSTROPHE   (US '"   / DE ä)
        ["backslash"] = 43,    // KEY_BACKSLASH    (US \|   / DE #')
        ["comma"] = 51,        // KEY_COMMA        (US ,<   / DE ,;)
        ["period"] = 52,       // KEY_DOT          (US .>   / DE .:)
        ["slash"] = 53,        // KEY_SLASH        (US /?   / DE -_)
        ["oem102"] = 86,       // KEY_102ND        (extra key next to left shift, DE <>|)

        // Numeric keypad
        ["numlock"] = 69,      // KEY_NUMLOCK
        ["num0"] = 82, ["num1"] = 79, ["num2"] = 80, ["num3"] = 81, ["num4"] = 75,
        ["num5"] = 76, ["num6"] = 77, ["num7"] = 71, ["num8"] = 72, ["num9"] = 73,
        ["numdivide"] = 98,    // KEY_KPSLASH
        ["nummultiply"] = 55,  // KEY_KPASTERISK
        ["numminus"] = 74,     // KEY_KPMINUS
        ["numplus"] = 78,      // KEY_KPPLUS
        ["numenter"] = 96,     // KEY_KPENTER
        ["numdecimal"] = 83,   // KEY_KPDOT

        // System keys
        ["printscreen"] = 99,  // KEY_SYSRQ
        ["scrolllock"] = 70,   // KEY_SCROLLLOCK
        ["pause"] = 119,       // KEY_PAUSE

        // Extended function keys (KEY_F13..KEY_F24)
        ["f13"] = 183, ["f14"] = 184, ["f15"] = 185, ["f16"] = 186, ["f17"] = 187,
        ["f18"] = 188, ["f19"] = 189, ["f20"] = 190, ["f21"] = 191, ["f22"] = 192,
        ["f23"] = 193, ["f24"] = 194,
    };

    // Canonical name -> Windows virtual-key code (VK_*) + extended-key flag.
    // Extended keys (right ctrl/alt, Win/Apps, navigation block, arrows) require
    // KEYEVENTF_EXTENDEDKEY when sent via SendInput.
    private static readonly Dictionary<string, (int virtualKey, bool extended)> Windows =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers
            ["ctrl"] = (0x11, false),   // VK_CONTROL
            ["rctrl"] = (0xA3, true),   // VK_RCONTROL
            ["shift"] = (0x10, false),  // VK_SHIFT
            ["rshift"] = (0xA1, false), // VK_RSHIFT
            ["alt"] = (0x12, false),    // VK_MENU
            ["altgr"] = (0xA5, true),   // VK_RMENU
            ["win"] = (0x5B, true),     // VK_LWIN
            ["menu"] = (0x5D, true),    // VK_APPS

            // Whitespace / control keys
            ["space"] = (0x20, false),     // VK_SPACE
            ["enter"] = (0x0D, false),     // VK_RETURN
            ["tab"] = (0x09, false),       // VK_TAB
            ["esc"] = (0x1B, false),       // VK_ESCAPE
            ["backspace"] = (0x08, false), // VK_BACK
            ["capslock"] = (0x14, false),  // VK_CAPITAL

            // Navigation block (extended)
            ["ins"] = (0x2D, true),      // VK_INSERT
            ["del"] = (0x2E, true),      // VK_DELETE
            ["home"] = (0x24, true),     // VK_HOME
            ["end"] = (0x23, true),      // VK_END
            ["pageup"] = (0x21, true),   // VK_PRIOR
            ["pagedown"] = (0x22, true), // VK_NEXT
            ["up"] = (0x26, true),       // VK_UP
            ["down"] = (0x28, true),     // VK_DOWN
            ["left"] = (0x25, true),     // VK_LEFT
            ["right"] = (0x27, true),    // VK_RIGHT

            // Function keys (VK_F1..VK_F12)
            ["f1"] = (0x70, false), ["f2"] = (0x71, false), ["f3"] = (0x72, false),
            ["f4"] = (0x73, false), ["f5"] = (0x74, false), ["f6"] = (0x75, false),
            ["f7"] = (0x76, false), ["f8"] = (0x77, false), ["f9"] = (0x78, false),
            ["f10"] = (0x79, false), ["f11"] = (0x7A, false), ["f12"] = (0x7B, false),

            // Letters (VK_A..VK_Z == ASCII upper-case)
            ["a"] = (0x41, false), ["b"] = (0x42, false), ["c"] = (0x43, false),
            ["d"] = (0x44, false), ["e"] = (0x45, false), ["f"] = (0x46, false),
            ["g"] = (0x47, false), ["h"] = (0x48, false), ["i"] = (0x49, false),
            ["j"] = (0x4A, false), ["k"] = (0x4B, false), ["l"] = (0x4C, false),
            ["m"] = (0x4D, false), ["n"] = (0x4E, false), ["o"] = (0x4F, false),
            ["p"] = (0x50, false), ["q"] = (0x51, false), ["r"] = (0x52, false),
            ["s"] = (0x53, false), ["t"] = (0x54, false), ["u"] = (0x55, false),
            ["v"] = (0x56, false), ["w"] = (0x57, false), ["x"] = (0x58, false),
            ["y"] = (0x59, false), ["z"] = (0x5A, false),

            // Digits (VK_0..VK_9 == ASCII digits)
            ["0"] = (0x30, false), ["1"] = (0x31, false), ["2"] = (0x32, false),
            ["3"] = (0x33, false), ["4"] = (0x34, false), ["5"] = (0x35, false),
            ["6"] = (0x36, false), ["7"] = (0x37, false), ["8"] = (0x38, false),
            ["9"] = (0x39, false),

            // Numeric keypad (VK_NUMPAD0..9 and the operator keys)
            ["numlock"] = (0x90, false),     // VK_NUMLOCK
            ["num0"] = (0x60, false), ["num1"] = (0x61, false), ["num2"] = (0x62, false),
            ["num3"] = (0x63, false), ["num4"] = (0x64, false), ["num5"] = (0x65, false),
            ["num6"] = (0x66, false), ["num7"] = (0x67, false), ["num8"] = (0x68, false),
            ["num9"] = (0x69, false),
            ["nummultiply"] = (0x6A, false), // VK_MULTIPLY
            ["numplus"] = (0x6B, false),     // VK_ADD
            ["numminus"] = (0x6D, false),    // VK_SUBTRACT
            ["numdecimal"] = (0x6E, false),  // VK_DECIMAL
            ["numdivide"] = (0x6F, true),    // VK_DIVIDE (extended)
            ["numenter"] = (0x0D, true),     // VK_RETURN on the keypad (extended)

            // System keys
            ["printscreen"] = (0x2C, true),  // VK_SNAPSHOT (extended)
            ["scrolllock"] = (0x91, false),  // VK_SCROLL
            ["pause"] = (0x13, false),       // VK_PAUSE

            // Extended function keys (VK_F13..VK_F24)
            ["f13"] = (0x7C, false), ["f14"] = (0x7D, false), ["f15"] = (0x7E, false),
            ["f16"] = (0x7F, false), ["f17"] = (0x80, false), ["f18"] = (0x81, false),
            ["f19"] = (0x82, false), ["f20"] = (0x83, false), ["f21"] = (0x84, false),
            ["f22"] = (0x85, false), ["f23"] = (0x86, false), ["f24"] = (0x87, false),

            // Note: no entries for the punctuation / OEM positions. Which VK_OEM_* code a
            // physical key carries depends on the active layout (the ü key is VK_OEM_1 on a
            // German board but VK_OEM_4 on a US one), so a fixed table would be wrong on half
            // the layouts. The SendInput backend resolves those positions from their scan code
            // via MapVirtualKey(MAPVK_VSC_TO_VK_EX), which always answers for the live layout.
        };

    // Canonical name -> PS/2 set-1 scan code + E0-extended flag (used by Interception).
    // Interception works at scan-code level, not virtual keys: the "make" code is sent with
    // state 0 (key down) / 1 (key up); the E0 flag adds 2 to the state for extended keys
    // (right ctrl/alt, Win/Apps, navigation block, arrows).
    private static readonly Dictionary<string, (int scanCode, bool e0)> Interception =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers
            ["ctrl"] = (0x1D, false),   // Left Ctrl
            ["rctrl"] = (0x1D, true),   // Right Ctrl (E0)
            ["shift"] = (0x2A, false),  // Left Shift
            ["rshift"] = (0x36, false), // Right Shift
            ["alt"] = (0x38, false),    // Left Alt
            ["altgr"] = (0x38, true),   // Right Alt / AltGr (E0)
            ["win"] = (0x5B, true),     // Left Win (E0)
            ["menu"] = (0x5D, true),    // Apps / context menu (E0)

            // Whitespace / control keys
            ["space"] = (0x39, false),
            ["enter"] = (0x1C, false),
            ["tab"] = (0x0F, false),
            ["esc"] = (0x01, false),
            ["backspace"] = (0x0E, false),
            ["capslock"] = (0x3A, false),

            // Navigation block (gray keys, all E0)
            ["ins"] = (0x52, true),
            ["del"] = (0x53, true),
            ["home"] = (0x47, true),
            ["end"] = (0x4F, true),
            ["pageup"] = (0x49, true),
            ["pagedown"] = (0x51, true),
            ["up"] = (0x48, true),
            ["down"] = (0x50, true),
            ["left"] = (0x4B, true),
            ["right"] = (0x4D, true),

            // Function keys
            ["f1"] = (0x3B, false), ["f2"] = (0x3C, false), ["f3"] = (0x3D, false),
            ["f4"] = (0x3E, false), ["f5"] = (0x3F, false), ["f6"] = (0x40, false),
            ["f7"] = (0x41, false), ["f8"] = (0x42, false), ["f9"] = (0x43, false),
            ["f10"] = (0x44, false), ["f11"] = (0x57, false), ["f12"] = (0x58, false),

            // Letters
            ["a"] = (0x1E, false), ["b"] = (0x30, false), ["c"] = (0x2E, false),
            ["d"] = (0x20, false), ["e"] = (0x12, false), ["f"] = (0x21, false),
            ["g"] = (0x22, false), ["h"] = (0x23, false), ["i"] = (0x17, false),
            ["j"] = (0x24, false), ["k"] = (0x25, false), ["l"] = (0x26, false),
            ["m"] = (0x32, false), ["n"] = (0x31, false), ["o"] = (0x18, false),
            ["p"] = (0x19, false), ["q"] = (0x10, false), ["r"] = (0x13, false),
            ["s"] = (0x1F, false), ["t"] = (0x14, false), ["u"] = (0x16, false),
            ["v"] = (0x2F, false), ["w"] = (0x11, false), ["x"] = (0x2D, false),
            ["y"] = (0x15, false), ["z"] = (0x2C, false),

            // Digits (number row)
            ["0"] = (0x0B, false), ["1"] = (0x02, false), ["2"] = (0x03, false),
            ["3"] = (0x04, false), ["4"] = (0x05, false), ["5"] = (0x06, false),
            ["6"] = (0x07, false), ["7"] = (0x08, false), ["8"] = (0x09, false),
            ["9"] = (0x0A, false),

            // Punctuation / OEM keys (US-legend positions, see the Linux table)
            ["grave"] = (0x29, false),
            ["minus"] = (0x0C, false),
            ["equals"] = (0x0D, false),
            ["leftbracket"] = (0x1A, false),
            ["rightbracket"] = (0x1B, false),
            ["semicolon"] = (0x27, false),
            ["quote"] = (0x28, false),
            ["backslash"] = (0x2B, false),
            ["comma"] = (0x33, false),
            ["period"] = (0x34, false),
            ["slash"] = (0x35, false),
            ["oem102"] = (0x56, false),

            // Numeric keypad. The non-E0 codes 0x47..0x53 are the keypad keys; the same codes
            // with E0 are the navigation block above (already mapped there).
            ["numlock"] = (0x45, false),
            ["num0"] = (0x52, false), ["num1"] = (0x4F, false), ["num2"] = (0x50, false),
            ["num3"] = (0x51, false), ["num4"] = (0x4B, false), ["num5"] = (0x4C, false),
            ["num6"] = (0x4D, false), ["num7"] = (0x47, false), ["num8"] = (0x48, false),
            ["num9"] = (0x49, false),
            ["numdivide"] = (0x35, true),
            ["nummultiply"] = (0x37, false),
            ["numminus"] = (0x4A, false),
            ["numplus"] = (0x4E, false),
            ["numenter"] = (0x1C, true),
            ["numdecimal"] = (0x53, false),

            // System keys. Pause is deliberately absent: it is the only key with a multi-byte
            // make sequence (E1 1D 45), which a single scan code cannot express.
            ["printscreen"] = (0x37, true),
            ["scrolllock"] = (0x46, false),

            // Extended function keys
            ["f13"] = (0x64, false), ["f14"] = (0x65, false), ["f15"] = (0x66, false),
            ["f16"] = (0x67, false), ["f17"] = (0x68, false), ["f18"] = (0x69, false),
            ["f19"] = (0x6A, false), ["f20"] = (0x6B, false), ["f21"] = (0x6C, false),
            ["f22"] = (0x6D, false), ["f23"] = (0x6E, false), ["f24"] = (0x76, false),
        };

    // Aliases -> canonical name.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["control"] = "ctrl",
        ["strg"] = "ctrl",
        ["ctl"] = "ctrl",
        ["rightctrl"] = "rctrl",
        ["rightshift"] = "rshift",
        ["rightalt"] = "altgr",
        ["alt gr"] = "altgr",
        ["windows"] = "win",
        ["super"] = "win",
        ["meta"] = "win",
        ["cmd"] = "win",
        ["command"] = "win",
        ["apps"] = "menu",
        ["context"] = "menu",
        ["escape"] = "esc",
        ["return"] = "enter",
        ["spacebar"] = "space",
        [" "] = "space",
        ["bksp"] = "backspace",
        ["entf"] = "del",
        ["delete"] = "del",
        ["insert"] = "ins",
        ["pgup"] = "pageup",
        ["pgdn"] = "pagedown",
        ["pgdown"] = "pagedown",
        ["arrowup"] = "up",
        ["arrowdown"] = "down",
        ["arrowleft"] = "left",
        ["arrowright"] = "right",

        // OEM names (as Windows/WPF names them on a US layout) -> position names. The bare
        // numeric forms Oem1..Oem7 are deliberately absent: they read as "the key Windows
        // calls VK_OEM_1", which is the ö key on a German layout but the ü key's US
        // counterpart here — a name that means two different keys is worse than no name.
        // Use the character ("ö") or the position ("Semicolon") instead.
        ["oemplus"] = "equals",
        ["oemminus"] = "minus",
        ["oemcomma"] = "comma",
        ["oemperiod"] = "period",
        ["oemsemicolon"] = "semicolon",
        ["oemquestion"] = "slash",
        ["oemtilde"] = "grave",
        ["oemopenbrackets"] = "leftbracket",
        ["oemclosebrackets"] = "rightbracket",
        ["oempipe"] = "backslash",
        ["oemquotes"] = "quote",
        ["oembackslash"] = "oem102",
        ["oem102nd"] = "oem102",
        ["102nd"] = "oem102",
        ["apostrophe"] = "quote",
        ["dot"] = "period",
        ["backtick"] = "grave",

        // Keypad
        ["numpad0"] = "num0",
        ["numpad1"] = "num1",
        ["numpad2"] = "num2",
        ["numpad3"] = "num3",
        ["numpad4"] = "num4",
        ["numpad5"] = "num5",
        ["numpad6"] = "num6",
        ["numpad7"] = "num7",
        ["numpad8"] = "num8",
        ["numpad9"] = "num9",
        ["numpadadd"] = "numplus",
        ["numpadsubtract"] = "numminus",
        ["numpadmultiply"] = "nummultiply",
        ["numpaddivide"] = "numdivide",
        ["numpaddecimal"] = "numdecimal",
        ["numpadenter"] = "numenter",
        ["add"] = "numplus",
        ["subtract"] = "numminus",
        ["multiply"] = "nummultiply",
        ["divide"] = "numdivide",
        ["decimal"] = "numdecimal",
        ["numlk"] = "numlock",

        // System keys
        ["print"] = "printscreen",
        ["prtsc"] = "printscreen",
        ["prtscn"] = "printscreen",
        ["snapshot"] = "printscreen",
        ["druck"] = "printscreen",
        ["scroll"] = "scrolllock",
        ["rollen"] = "scrolllock",
        ["break"] = "pause",
    };

    private static string Normalize(string name)
    {
        var key = name.Trim();
        return Aliases.TryGetValue(key, out var canonical) ? canonical : key;
    }

    /// <summary>
    /// Resolves a key name to a stable lower-case token (aliases applied), so names that
    /// mean the same key compare equal regardless of spelling or casing — e.g. "Escape",
    /// "escape" and "Esc" all map to "esc". Used for hotkey matching.
    /// </summary>
    public static string Canonicalize(string name)
    {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : Normalize(name).ToLowerInvariant();
    }

    /// <summary>
    /// True when the name denotes a single character (e.g. "ü", "#", "+", "z") rather than a
    /// name from the tables. Such names are resolved by the backends against the active
    /// keyboard layout, which is the only thing that knows where the character lives.
    /// The character is lower-cased so "Ü" and "ü" mean the same key without an added Shift.
    /// </summary>
    public static bool TryGetCharacter(string name, out char character)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 1)
        {
            character = char.ToLowerInvariant(trimmed[0]);
            return true;
        }

        character = '\0';
        return false;
    }

    /// <summary>Resolves a key name to its Linux evdev key code.</summary>
    public static bool TryGetLinux(string name, out int keyCode)
    {
        return Linux.TryGetValue(Normalize(name), out keyCode);
    }

    /// <summary>Resolves a key name to its Windows virtual-key code (VK_*) and extended flag.</summary>
    public static bool TryGetWindows(string name, out int virtualKey, out bool extended)
    {
        if (Windows.TryGetValue(Normalize(name), out var entry))
        {
            virtualKey = entry.virtualKey;
            extended = entry.extended;
            return true;
        }

        virtualKey = 0;
        extended = false;
        return false;
    }

    /// <summary>Resolves a key name to its PS/2 set-1 scan code and E0-extended flag (for Interception).</summary>
    public static bool TryGetInterception(string name, out int scanCode, out bool e0)
    {
        if (Interception.TryGetValue(Normalize(name), out var entry))
        {
            scanCode = entry.scanCode;
            e0 = entry.e0;
            return true;
        }

        scanCode = 0;
        e0 = false;
        return false;
    }

    /// <summary>All Linux evdev key codes used by the name table (for uinput keybit registration).</summary>
    public static IEnumerable<int> AllLinuxKeyCodes => Linux.Values;

    // Reverse of the Linux table (code -> canonical name); codes are unique so this is 1:1.
    private static readonly Lazy<Dictionary<int, string>> LinuxReverse = new(() =>
    {
        var map = new Dictionary<int, string>();
        foreach (var pair in Linux)
            map.TryAdd(pair.Value, pair.Key);
        return map;
    });

    /// <summary>Resolves a Linux evdev key code back to its canonical key name (for recording).</summary>
    public static bool TryGetLinuxName(int keyCode, out string name)
    {
        return LinuxReverse.Value.TryGetValue(keyCode, out name);
    }
}
