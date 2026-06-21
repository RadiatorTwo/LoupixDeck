using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Windows;

/// <summary>
/// Contains message information from a thread's message queue.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    /// <summary>
    /// A handle to the window whose window procedure receives the message.
    /// This member is <see cref="IntPtr.Zero"/> when the message is a thread message.
    /// </summary>
    public IntPtr hwnd;

    /// <summary>
    /// The message identifier. Applications can only use the low word; the high word is reserved by the system.
    /// </summary>
    public uint message;

    /// <summary>
    /// Additional information about the message.
    /// The exact meaning depends on the value of the message member.
    /// </summary>
    public IntPtr wParam;

    /// <summary>
    /// Additional information about the message.
    /// The exact meaning depends on the value of the message member.
    /// </summary>
    public IntPtr lParam;

    /// <summary>
    /// The time at which the message was posted.
    /// </summary>
    public uint time;

    /// <summary>
    /// The cursor position, in screen coordinates, when the message was posted.
    /// </summary>
    public POINT pt;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Reserved for windows")]
    private uint lPrivate;
}
