using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
#if WINDOWS
using System.Runtime.InteropServices;
using Avalonia.Platform;
#endif

namespace LoupixDeck.Utils;

/// <summary>
/// Window geometry helpers the platform-neutral API lacks: moving a window's left edge and setting
/// its width in one step, its frame in screen pixels, and a picture of a part of the screen.
/// </summary>
/// <remarks>
/// Avalonia moves and resizes a window in two separate calls. Growing a window to the left takes
/// both, and between them the window shows for a frame at its new position with its old width,
/// so whatever sits in the middle of it visibly jumps. Win32 does both in one SetWindowPos.
/// </remarks>
public static partial class WindowBounds
{
    /// <summary>
    /// Places the window's frame at <paramref name="x"/> (pixels, as <see cref="Window.Position"/>)
    /// with a client width of <paramref name="clientWidth"/> (logical units, as <see cref="Layoutable.Width"/>).
    /// Returns false where it cannot be done in one step; the caller then sets both on the window.
    /// </summary>
    public static bool TrySetLeftAndWidth(Window window, int x, double clientWidth)
    {
#if WINDOWS
        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect frame) || !GetClientRect(hwnd, out Rect client))
            return false;

        // Relative to where Avalonia reports the window, so its own frame offsets carry over.
        int left = frame.Left + (x - window.Position.X);
        int clientPixels = (int)Math.Round(clientWidth * window.RenderScaling);
        int width = (frame.Right - frame.Left) + (clientPixels - (client.Right - client.Left));

        return SetWindowPos(hwnd, IntPtr.Zero, left, frame.Top, width, frame.Bottom - frame.Top,
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
#else
        return false;
#endif
    }

    /// <summary>The window's frame in screen pixels, or null where the platform does not tell.</summary>
    public static PixelRect? GetFrame(Window window)
    {
#if WINDOWS
        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect frame)) return null;

        return new PixelRect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
#else
        return null;
#endif
    }

    /// <summary>
    /// The visible part of the window's frame in screen pixels, without the invisible resize borders
    /// the frame from <see cref="GetFrame"/> includes, or null where the platform does not tell.
    /// </summary>
    public static PixelRect? GetVisibleFrame(Window window)
    {
#if WINDOWS
        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero ||
            DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out Rect frame, Marshal.SizeOf<Rect>()) != 0)
            return null;

        return new PixelRect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
#else
        return null;
#endif
    }

    /// <summary>
    /// What the screen shows in <paramref name="area"/> (screen pixels) right now, or null where
    /// the platform cannot tell.
    /// </summary>
    public static WriteableBitmap CaptureScreen(PixelRect area)
    {
#if WINDOWS
        if (area.Width <= 0 || area.Height <= 0) return null;

        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;

        IntPtr memory = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) return null;

            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy)) return null;

            // Top-down 32-bit rows: a negative height asks for the first row first.
            BitmapInfoHeader header = new()
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = area.Width,
                Height = -area.Height,
                Planes = 1,
                BitCount = 32,
            };
            byte[] pixels = new byte[area.Width * area.Height * 4];
            SelectObject(memory, previous);
            previous = IntPtr.Zero;
            if (GetDIBits(memory, bitmap, 0, (uint)area.Height, pixels, ref header, DibRgbColors) == 0) return null;

            // GDI leaves the alpha byte empty.
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            WriteableBitmap result = new(new PixelSize(area.Width, area.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using ILockedFramebuffer target = result.Lock();
            int rowBytes = area.Width * 4;
            for (int row = 0; row < area.Height; row++)
                Marshal.Copy(pixels, row * rowBytes, target.Address + (row * target.RowBytes), rowBytes);

            return result;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
#else
        return null;
#endif
    }

#if WINDOWS
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SrcCopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const int DwmwaExtendedFrameBounds = 9;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
#endif
}
