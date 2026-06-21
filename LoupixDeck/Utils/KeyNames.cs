using System.Collections.Frozen;
using System.Collections.Immutable;

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
/// </summary>
public static class KeyNames
{
    // Canonical name -> Linux evdev key code (see input-event-codes.h).
    private static readonly FrozenDictionary<string, ushort> Linux = FrozenDictionary.ToFrozenDictionary<string, ushort>(
    [
        // Modifiers
        new("ctrl", 29),        // KEY_LEFTCTRL
        new("rctrl", 97),       // KEY_RIGHTCTRL
        new("shift", 42),       // KEY_LEFTSHIFT
        new("rshift", 54),      // KEY_RIGHTSHIFT
        new("alt", 56),         // KEY_LEFTALT
        new("altgr", 100),      // KEY_RIGHTALT
        new("win", 125),        // KEY_LEFTMETA
        new("menu", 127),       // KEY_COMPOSE (context menu / apps key)

        // Whitespace / control keys
        new("space", 57),       // KEY_SPACE
        new("enter", 28),       // KEY_ENTER
        new("tab", 15),         // KEY_TAB
        new("esc", 1),          // KEY_ESC
        new("backspace", 14),   // KEY_BACKSPACE
        new("capslock", 58),    // KEY_CAPSLOCK

        // Navigation block
        new("ins", 110),        // KEY_INSERT
        new("del", 111),        // KEY_DELETE
        new("home", 102),       // KEY_HOME
        new("end", 107),        // KEY_END
        new("pageup", 104),     // KEY_PAGEUP
        new("pagedown", 109),   // KEY_PAGEDOWN
        new("up", 103),         // KEY_UP
        new("down", 108),       // KEY_DOWN
        new("left", 105),       // KEY_LEFT
        new("right", 106),      // KEY_RIGHT

        // Function keys
        new("f1", 59), new("f2", 60), new("f3", 61), new("f4", 62), new("f5", 63), new("f6", 64),
        new("f7", 65), new("f8", 66), new("f9", 67), new("f10", 68), new("f11", 87), new("f12", 88),

        // Letters
        new("a", 30), new("b", 48), new("c", 46), new("d", 32), new("e", 18), new("f", 33), new("g", 34),
        new("h", 35), new("i", 23), new("j", 36), new("k", 37), new("l", 38), new("m", 50), new("n", 49),
        new("o", 24), new("p", 25), new("q", 16), new("r", 19), new("s", 31), new("t", 20), new("u", 22),
        new("v", 47), new("w", 17), new("x", 45), new("y", 21), new("z", 44),

        // Digits (number row)
        new("0", 11), new("1", 2), new("2", 3), new("3", 4), new("4", 5), new("5", 6), new("6", 7),
        new("7", 8), new("8", 9), new("9", 10),
    ], StringComparer.OrdinalIgnoreCase);

    // Reverse of the Linux table (code -> canonical name); codes are unique so this is 1:1.
    private static readonly FrozenDictionary<ushort, string> LinuxReverse = Linux.DistinctBy(static kvp => kvp.Value).ToFrozenDictionary(static kvp => kvp.Value, static kvp => kvp.Key);

    // Canonical name -> Windows virtual-key code (VK_*) + extended-key flag.
    // Extended keys (right ctrl/alt, Win/Apps, navigation block, arrows) require
    // KEYEVENTF_EXTENDEDKEY when sent via SendInput.
    private static readonly FrozenDictionary<string, (ushort virtualKey, bool extended)> Windows =
        new Dictionary<string, (ushort virtualKey, bool extended)>(StringComparer.OrdinalIgnoreCase)
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
        }.ToFrozenDictionary();

    // Canonical name -> PS/2 set-1 scan code + E0-extended flag (used by Interception).
    // Interception works at scan-code level, not virtual keys: the "make" code is sent with
    // state 0 (key down) / 1 (key up); the E0 flag adds 2 to the state for extended keys
    // (right ctrl/alt, Win/Apps, navigation block, arrows).
    private static readonly FrozenDictionary<string, (ushort scanCode, bool e0)> Interception =
        new Dictionary<string, (ushort scanCode, bool e0)>(StringComparer.OrdinalIgnoreCase)
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
        }.ToFrozenDictionary();

    // Aliases -> canonical name.
    private static readonly FrozenDictionary<string, string> Aliases = FrozenDictionary.ToFrozenDictionary<string, string>(
    [
        new("control", "ctrl"),
        new("strg", "ctrl"),
        new("ctl", "ctrl"),
        new("rightctrl", "rctrl"),
        new("rightshift", "rshift"),
        new("rightalt", "altgr"),
        new("alt gr", "altgr"),
        new("windows", "win"),
        new("super", "win"),
        new("meta", "win"),
        new("cmd", "win"),
        new("command", "win"),
        new("apps", "menu"),
        new("context", "menu"),
        new("escape", "esc"),
        new("return", "enter"),
        new("spacebar", "space"),
        new(" ", "space"),
        new("bksp", "backspace"),
        new("entf", "del"),
        new("delete", "del"),
        new("insert", "ins"),
        new("pgup", "pageup"),
        new("pgdn", "pagedown"),
        new("pgdown", "pagedown"),
        new("arrowup", "up"),
        new("arrowdown", "down"),
        new("arrowleft", "left"),
        new("arrowright", "right"),
    ], StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Resolves a key name to its Linux evdev key code.</summary>
    public static bool TryGetLinux(string name, out ushort keyCode)
    {
        return Linux.TryGetValue(Normalize(name), out keyCode);
    }

    /// <summary>Resolves a key name to its Windows virtual-key code (VK_*) and extended flag.</summary>
    public static bool TryGetWindows(string name, out ushort virtualKey, out bool extended)
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
    public static ImmutableArray<ushort> AllLinuxKeyCodes => Linux.Values;

    /// <summary>Resolves a Linux evdev key code back to its canonical key name (for recording).</summary>
    public static bool TryGetLinuxName(ushort keyCode, out string name) => LinuxReverse.TryGetValue(keyCode, out name);
}
