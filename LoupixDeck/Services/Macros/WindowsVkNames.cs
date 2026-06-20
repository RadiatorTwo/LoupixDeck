namespace LoupixDeck.Services.Macros;

/// <summary>
/// Maps the Windows virtual-key codes delivered by the low-level keyboard hook to the
/// canonical macro key names (the names <see cref="Utils.KeyNames"/> understands). Unlike
/// the forward table in KeyNames, the hook reports the side-specific modifier VKs
/// (VK_LCONTROL/VK_RCONTROL, …), so those are mapped explicitly here.
/// </summary>
internal static class WindowsVkNames
{
    private static readonly Dictionary<int, string> Special = new()
    {
        // Side-specific modifiers as reported by the low-level hook
        [0xA0] = "Shift", [0xA1] = "RShift",
        [0xA2] = "Ctrl", [0xA3] = "RCtrl",
        [0xA4] = "Alt", [0xA5] = "AltGr",
        [0x5B] = "Win", [0x5C] = "Win", [0x5D] = "Menu",

        // Whitespace / control keys
        [0x20] = "Space", [0x0D] = "Enter", [0x09] = "Tab", [0x1B] = "Esc",
        [0x08] = "Backspace", [0x14] = "CapsLock",

        // Navigation block
        [0x2D] = "Ins", [0x2E] = "Del", [0x24] = "Home", [0x23] = "End",
        [0x21] = "PageUp", [0x22] = "PageDown",
        [0x26] = "Up", [0x28] = "Down", [0x25] = "Left", [0x27] = "Right",

        // Function keys
        [0x70] = "F1", [0x71] = "F2", [0x72] = "F3", [0x73] = "F4",
        [0x74] = "F5", [0x75] = "F6", [0x76] = "F7", [0x77] = "F8",
        [0x78] = "F9", [0x79] = "F10", [0x7A] = "F11", [0x7B] = "F12",
    };

    public static bool TryGetName(int vk, out string name)
    {
        if (Special.TryGetValue(vk, out name))
            return true;

        // Letters (VK_A..VK_Z == ASCII upper-case) and digits (VK_0..VK_9 == ASCII digits).
        if (vk is >= 0x41 and <= 0x5A || vk is >= 0x30 and <= 0x39)
        {
            name = ((char)vk).ToString();
            return true;
        }

        name = null;
        return false;
    }
}
