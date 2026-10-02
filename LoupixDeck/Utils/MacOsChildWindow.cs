using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace LoupixDeck.Utils;

/// <summary>
/// Makes one window an AppKit child of another, so that the window server moves it with its
/// parent. A no-op on every other platform.
/// </summary>
/// <remarks>
/// Avalonia's <see cref="Window.Show(Window)"/> records the owner for z-ordering only: its macOS
/// backend never calls <c>addChildWindow:</c>, so an owned window stays where it is when its owner
/// is dragged and has to be chased from a <see cref="WindowBase.PositionChanged"/> handler — one
/// move behind the owner, on every mouse event, which is what read as judder. A real child window
/// is moved by the window server in the same transaction as its parent.
///
/// Ordering a child window out detaches it from its parent, so a hidden panel is never dragged
/// back in by AppKit; <see cref="Detach"/> makes that explicit before a hide all the same. The
/// relationship therefore has to be re-made after every show.
/// </remarks>
public static partial class MacOsChildWindow
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSWindowOrderingMode
    private const long NSWindowAbove = 1;

    /// <summary>Whether this platform parents windows this way at all.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    public static void Attach(Window parent, Window child)
    {
        if (!IsSupported) return;

        IntPtr parentWindow = NSWindow(parent);
        IntPtr childWindow = NSWindow(child);
        if (parentWindow == IntPtr.Zero || childWindow == IntPtr.Zero) return;

        AddChildWindow(parentWindow, sel_registerName("addChildWindow:ordered:"), childWindow, NSWindowAbove);
    }

    public static void Detach(Window parent, Window child)
    {
        if (!IsSupported) return;

        IntPtr parentWindow = NSWindow(parent);
        IntPtr childWindow = NSWindow(child);
        if (parentWindow == IntPtr.Zero || childWindow == IntPtr.Zero) return;

        RemoveChildWindow(parentWindow, sel_registerName("removeChildWindow:"), childWindow);
    }

    /// <summary>The NSWindow behind an Avalonia window; the handle Avalonia.Native hands out for a top-level.</summary>
    private static IntPtr NSWindow(Window window)
    {
        IPlatformHandle handle = window.TryGetPlatformHandle();
        return handle?.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    // objc_msgSend is declared once per message signature, as the ABI requires.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void AddChildWindow(IntPtr receiver, IntPtr selector, IntPtr child, long ordered);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void RemoveChildWindow(IntPtr receiver, IntPtr selector, IntPtr child);
}
