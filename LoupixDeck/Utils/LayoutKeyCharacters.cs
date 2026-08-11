using System.Runtime.InteropServices;

namespace LoupixDeck.Utils;

/// <summary>
/// Answers which character a physical key types when no modifier is held, according to the
/// keyboard layout that is active right now. Key capture uses it to name a punctuation key
/// after the character it produces even when the press itself produced none — holding Ctrl
/// yields a control character rather than a symbol, which would otherwise make Ctrl+Ü and
/// Shift+Ü end up with different names for the same key.
///
/// Windows only; elsewhere no character is reported and the caller falls back to the
/// position name.
/// </summary>
internal static class LayoutKeyCharacters
{
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private const uint MapVkVscToVkEx = 3; // scan code -> virtual key, honouring E0
    private const uint MapVkVkToChar = 2;  // virtual key -> unshifted character

    // MapVirtualKey sets the top bit of the result for a dead key.
    private const uint DeadKeyFlag = 0x80000000;

    /// <summary>
    /// Resolves the unmodified character of the key at the given PS/2 set-1 scan code.
    /// Returns false when the platform cannot answer, the key types nothing, or it is a dead
    /// key (^ and ´ on a German board) — a dead key produces no character on its own, so it
    /// keeps its position name.
    /// </summary>
    public static bool TryGetUnmodified(int scanCode, bool e0, out char character)
    {
        character = '\0';

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            // The capture happens in this app's own window, so this thread's layout is the
            // one the user is typing on.
            var layout = GetKeyboardLayout(0);

            var virtualKey = MapVirtualKeyEx(e0 ? 0xE000u | (uint)scanCode : (uint)scanCode,
                MapVkVscToVkEx, layout);
            if (virtualKey == 0)
                return false;

            var result = MapVirtualKeyEx(virtualKey, MapVkVkToChar, layout);
            if (result == 0 || (result & DeadKeyFlag) != 0)
                return false;

            var value = (char)(result & 0xFFFF);
            if (char.IsControl(value) || char.IsWhiteSpace(value))
                return false;

            character = char.ToLowerInvariant(value);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
