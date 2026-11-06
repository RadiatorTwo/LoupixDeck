using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LoupixDeck.WaylandFocus.Activation;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Focuses a window through the EWMH conventions every X11 window manager implements:
/// <c>_NET_CLIENT_LIST</c> lists the windows, <c>_NET_WM_PID</c> and <c>WM_CLASS</c> identify
/// them, <c>_NET_ACTIVE_WINDOW</c> names the focused one and, sent as a client message to the
/// root window, asks the window manager to activate another — what <c>wmctrl -a</c> does,
/// without depending on it being installed. On a Wayland session this reaches XWayland windows
/// only.
/// </summary>
/// <remarks>
/// Xlib's default error handler terminates the process, and a window can vanish between listing
/// and reading it (BadWindow). A no-op handler is installed for the duration of the call and the
/// previous one — Avalonia's — restored afterwards.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed unsafe partial class X11WindowActivator : IWindowActivator
{
    private const string LibX11 = "libX11.so.6";

    private const int ClientMessage = 33;
    private const nint SubstructureNotifyMask = 1 << 19;
    private const nint SubstructureRedirectMask = 1 << 20;
    private const int Success = 0;
    private const nuint AnyPropertyType = 0;

    /// <summary>_NET_ACTIVE_WINDOW source indication "pager": a request on the user's behalf,
    /// which window managers honour without focus-stealing prevention.</summary>
    private const nint SourcePager = 2;

    private static readonly Lock Gate = new();

    public string Compositor => "x11";

    public Task<WindowActivationResult> ActivateAsync(
        Func<ToplevelWindow, bool> match, CancellationToken cancellationToken)
        => Task.Run(() => Activate(match), cancellationToken);

    private static WindowActivationResult Activate(Func<ToplevelWindow, bool> match)
    {
        lock (Gate)
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
                throw new InvalidOperationException("cannot open the X display");

            IntPtr previousHandler = XSetErrorHandler(&IgnoreError);
            try
            {
                return Activate(display, match);
            }
            finally
            {
                XSetErrorHandler((delegate* unmanaged<IntPtr, IntPtr, int>)previousHandler);
                XCloseDisplay(display);
            }
        }
    }

    private static WindowActivationResult Activate(IntPtr display, Func<ToplevelWindow, bool> match)
    {
        nuint root = XDefaultRootWindow(display);
        nuint clientList = XInternAtom(display, "_NET_CLIENT_LIST_STACKING", 0);
        nuint activeAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", 0);
        nuint pidAtom = XInternAtom(display, "_NET_WM_PID", 0);
        nuint classAtom = XInternAtom(display, "WM_CLASS", 0);
        nuint nameAtom = XInternAtom(display, "_NET_WM_NAME", 0);

        // Stacking order puts the most recently raised window last; fall back to the plain list.
        nuint[] windows = ReadLongs(display, root, clientList);
        if (windows.Length == 0)
            windows = ReadLongs(display, root, XInternAtom(display, "_NET_CLIENT_LIST", 0));
        else
            Array.Reverse(windows);

        nuint[] active = ReadLongs(display, root, activeAtom);
        nuint focused = active.Length > 0 ? active[0] : 0;

        (WindowActivationResult result, Boxed target) = WindowPicker.Pick(
            windows.Select(window =>
            {
                nuint[] pid = ReadLongs(display, window, pidAtom);
                return (new ToplevelWindow(
                    AppId: WindowClass(display, window, classAtom),
                    Title: ReadString(display, window, nameAtom),
                    Pid: pid.Length > 0 ? (int)pid[0] : null,
                    IsFocused: window == focused), new Boxed(window));
            }),
            match);

        if (target is null)
            return result;

        XClientMessageEvent message = new()
        {
            Type = ClientMessage,
            SendEvent = 1,
            Display = display,
            Window = target.Window,
            MessageType = activeAtom,
            Format = 32,
            Data0 = SourcePager,
            Data1 = 0, // CurrentTime
            Data2 = (nint)focused
        };
        XSendEvent(display, root, 0, SubstructureRedirectMask | SubstructureNotifyMask, &message);
        XFlush(display);
        return result;
    }

    /// <summary>The class half of WM_CLASS ("instance\0class\0"), or the instance when absent.</summary>
    private static string WindowClass(IntPtr display, nuint window, nuint classAtom)
    {
        byte[] raw = ReadBytes(display, window, classAtom);
        string[] parts = Encoding.UTF8.GetString(raw).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => null,
            1 => parts[0],
            _ => parts[1]
        };
    }

    private static string ReadString(IntPtr display, nuint window, nuint property)
    {
        byte[] raw = ReadBytes(display, window, property);
        return raw.Length == 0 ? null : Encoding.UTF8.GetString(raw).TrimEnd('\0');
    }

    /// <summary>
    /// A format-32 property. Xlib returns those as C <c>long</c>s — 8 bytes on 64-bit Linux,
    /// whatever the 32 in the format says.
    /// </summary>
    private static nuint[] ReadLongs(IntPtr display, nuint window, nuint property)
    {
        if (!GetProperty(display, window, property, out int format, out nuint count, out IntPtr data))
            return [];

        try
        {
            if (format != 32)
                return [];

            nuint[] values = new nuint[(int)count];
            for (int i = 0; i < values.Length; i++)
                values[i] = ((nuint*)data)[i];
            return values;
        }
        finally
        {
            XFree(data);
        }
    }

    private static byte[] ReadBytes(IntPtr display, nuint window, nuint property)
    {
        if (!GetProperty(display, window, property, out int format, out nuint count, out IntPtr data))
            return [];

        try
        {
            return format == 8 ? new ReadOnlySpan<byte>((void*)data, (int)count).ToArray() : [];
        }
        finally
        {
            XFree(data);
        }
    }

    private static bool GetProperty(IntPtr display, nuint window, nuint property,
        out int format, out nuint count, out IntPtr data)
    {
        int status = XGetWindowProperty(display, window, property, 0, 1 << 16, 0, AnyPropertyType,
            out _, out format, out count, out _, out data);
        if (status == Success && data != IntPtr.Zero)
            return true;

        if (data != IntPtr.Zero)
            XFree(data);
        data = IntPtr.Zero;
        return false;
    }

    [UnmanagedCallersOnly]
    private static int IgnoreError(IntPtr display, IntPtr errorEvent) => 0;

    /// <summary>A window id as a reference type, which the picker hands back.</summary>
    private sealed record Boxed(nuint Window);

    /// <summary>
    /// XClientMessageEvent, padded to the size of the XEvent union it is read as (24 longs).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public nint Data0;
        public nint Data1;
        public nint Data2;
        public nint Data3;
        public nint Data4;
    }

    [LibraryImport(LibX11)]
    private static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport(LibX11)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial nuint XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(IntPtr display, string name, int onlyIfExists);

    [LibraryImport(LibX11)]
    private static partial int XGetWindowProperty(IntPtr display, nuint window, nuint property,
        nint offset, nint length, int delete, nuint requestedType,
        out nuint actualType, out int actualFormat, out nuint itemCount, out nuint bytesAfter, out IntPtr data);

    [LibraryImport(LibX11)]
    private static partial int XFree(IntPtr data);

    [LibraryImport(LibX11)]
    private static partial int XSendEvent(IntPtr display, nuint window, int propagate, nint eventMask,
        XClientMessageEvent* sentEvent);

    [LibraryImport(LibX11)]
    private static partial int XFlush(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial IntPtr XSetErrorHandler(delegate* unmanaged<IntPtr, IntPtr, int> handler);
}
