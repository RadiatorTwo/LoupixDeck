using System.Runtime.InteropServices;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Mouse injection on macOS through Quartz Event Services, the counterpart to
/// <c>MacOsQuartzKeyboard</c> and subject to the same Accessibility grant: without it every
/// event is discarded silently.
/// </summary>
/// <remarks>
/// Unlike uinput, macOS has no relative-motion event — a move is always posted as an absolute
/// position. <see cref="MoveRelative"/> therefore reads the current cursor location and adds
/// the delta, which also means macOS supports <see cref="MoveAbsolute"/> natively where Linux
/// does not.
/// </remarks>
public sealed partial class MacOsQuartzMouse : IVirtualMouse
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint HidEventTap = 0;

    // CGEventType
    private const uint LeftMouseDown = 1;
    private const uint LeftMouseUp = 2;
    private const uint RightMouseDown = 3;
    private const uint RightMouseUp = 4;
    private const uint MouseMoved = 5;
    private const uint OtherMouseDown = 25;
    private const uint OtherMouseUp = 26;

    // kCGScrollEventUnitLine — detents, which is what the interface promises.
    private const uint ScrollUnitLine = 1;

    private bool _warnedUntrusted;

    public MacOsQuartzMouse()
    {
        Connected = AXIsProcessTrusted();
    }

    public bool Connected { get; private set; }

    public void Click(MouseButton button)
    {
        ButtonDown(button);
        ButtonUp(button);
    }

    public void ButtonDown(MouseButton button)
    {
        if (!Ready()) return;

        (uint downType, _, uint number) = Map(button);
        PostMouse(downType, CursorLocation(), number);
    }

    public void ButtonUp(MouseButton button)
    {
        if (!Ready()) return;

        (_, uint upType, uint number) = Map(button);
        PostMouse(upType, CursorLocation(), number);
    }

    public void MoveRelative(int dx, int dy)
    {
        if (!Ready()) return;

        CGPoint current = CursorLocation();
        PostMouse(MouseMoved, new CGPoint { X = current.X + dx, Y = current.Y + dy }, 0);
    }

    public void MoveAbsolute(int x, int y)
    {
        if (!Ready()) return;

        PostMouse(MouseMoved, new CGPoint { X = x, Y = y }, 0);
    }

    public void Scroll(int amount)
    {
        if (!Ready() || amount == 0) return;

        IntPtr handle = CGEventCreateScrollWheelEvent(IntPtr.Zero, ScrollUnitLine, 1, amount);
        if (handle == IntPtr.Zero)
            return;

        CGEventPost(HidEventTap, handle);
        CFRelease(handle);
    }

    public void Dispose()
    {
    }

    /// <summary>Down event, up event and CGMouseButton number for a button.</summary>
    private static (uint down, uint up, uint number) Map(MouseButton button) => button switch
    {
        MouseButton.Left => (LeftMouseDown, LeftMouseUp, 0u),
        MouseButton.Right => (RightMouseDown, RightMouseUp, 1u),
        MouseButton.Middle => (OtherMouseDown, OtherMouseUp, 2u),
        MouseButton.X1 => (OtherMouseDown, OtherMouseUp, 3u),
        _ => (OtherMouseDown, OtherMouseUp, 4u)
    };

    private static void PostMouse(uint type, CGPoint position, uint button)
    {
        IntPtr handle = CGEventCreateMouseEvent(IntPtr.Zero, type, position, button);
        if (handle == IntPtr.Zero)
            return;

        CGEventPost(HidEventTap, handle);
        CFRelease(handle);
    }

    /// <summary>
    /// Where the cursor is now. A null-source event carries the current location, which is the
    /// cheapest way to read it without pulling in AppKit.
    /// </summary>
    private static CGPoint CursorLocation()
    {
        IntPtr handle = CGEventCreate(IntPtr.Zero);
        if (handle == IntPtr.Zero)
            return new CGPoint();

        CGPoint point = CGEventGetLocation(handle);
        CFRelease(handle);
        return point;
    }

    private bool Ready()
    {
        if (Connected)
            return true;

        Connected = AXIsProcessTrusted();
        if (Connected)
            return true;

        if (!_warnedUntrusted)
        {
            _warnedUntrusted = true;
            Console.WriteLine("[Mouse] Mouse output skipped: no Accessibility permission.");
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(ApplicationServices)]
    private static partial IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType,
        CGPoint mouseCursorPosition, uint mouseButton);

    [LibraryImport(ApplicationServices)]
    private static partial IntPtr CGEventCreateScrollWheelEvent(IntPtr source, uint units,
        uint wheelCount, int wheel1);

    [LibraryImport(ApplicationServices)]
    private static partial IntPtr CGEventCreate(IntPtr source);

    [LibraryImport(ApplicationServices)]
    private static partial CGPoint CGEventGetLocation(IntPtr handle);

    [LibraryImport(ApplicationServices)]
    private static partial void CGEventPost(uint tap, IntPtr handle);

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr handle);
}
