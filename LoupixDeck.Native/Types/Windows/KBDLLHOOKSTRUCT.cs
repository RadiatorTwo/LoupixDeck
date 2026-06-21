using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Windows;

/// <summary>
/// Contains information about a low-level keyboard input event.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    /// <summary>
    /// A virtual-key code.
    /// </summary>
    /// <remarks>
    /// The code must be a value in the range 1 to 254.
    /// </remarks>
    public uint vkCode;

    /// <summary>
    /// A hardware scan code for the key.
    /// </summary>
    public uint scanCode;
    /// <summary>
    /// The extended-key flag, event-injected flags, context code, and transition-state flag.
    /// This member is specified as follows.
    /// An application can use the following values to test the keystroke flags.
    /// Testing LLKHF_INJECTED (bit 4) will tell you whether the event was injected.
    /// If it was, then testing LLKHF_LOWER_IL_INJECTED (bit 1) will tell you whether or not the event was injected from a process running at lower integrity level.
    /// </summary>
    public uint flags;

    /// <summary>
    /// Specifies whether the key is an extended key, such as a function key or a key on the numeric keypad.
    /// </summary>
    public readonly bool IsExtendedKey => (flags & 0b0000_0001) is not 0;
    /// <summary>
    /// Specifies whether the event was injected. Note that <see cref="IsInjectedLowerIntegrity"/> is not necessarily true when this is.
    /// </summary>
    public readonly bool IsInjected => (flags & 0x00000010) is not 0;
    /// <summary>
    /// Specifies whether the event was injected from a process running at lower integrity level. Note that <see cref="IsInjected"/> is also true when this is.
    /// </summary>
    public readonly bool IsInjectedLowerIntegrity => (flags & 0x00000002) is not 0;

    public readonly bool IsAltPressed => (flags & 0b0010_0000) is not 0;
    public readonly bool IsAltReleased => !IsPressed;

    public readonly bool IsPressed => (flags & 0b1000_0000) is 0;
    public readonly bool IsReleased => !IsPressed;

    /// <summary>
    /// The time stamp for this message, equivalent to what GetMessageTime would return for this message.
    /// </summary>
    public uint time;

    /// <summary>
    /// Additional information associated with the message.
    /// </summary>
    public IntPtr dwExtraInfo;
}
