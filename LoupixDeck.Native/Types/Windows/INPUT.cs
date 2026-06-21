using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Windows;

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public uint type;
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;

    public static class Create
    {
        public static class Constants
        {
            public const uint MOUSEEVENTF_MOVE = 0x0001;
            public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            public const uint MOUSEEVENTF_LEFTUP = 0x0004;
            public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
            public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
            public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
            public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
            public const uint MOUSEEVENTF_WHEEL = 0x0800;
            public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
            public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

            public const int WHEEL_DELTA = 120;
        }

        public static MOUSEINPUT Button(uint buttonFlags)
            => Impl(0, 0, 0, buttonFlags);

        public static MOUSEINPUT Move(int dx, int dy)
            => Impl(dx, dy, 0, Constants.MOUSEEVENTF_MOVE);

        public static MOUSEINPUT Scroll(int scrollAmount)
            => Impl(0, 0, unchecked((uint)(scrollAmount * Constants.WHEEL_DELTA)), Constants.MOUSEEVENTF_WHEEL);

        public static MOUSEINPUT AbsoluteMovement(int nx, int ny)
            => Impl(nx, ny, 0, Constants.MOUSEEVENTF_MOVE | Constants.MOUSEEVENTF_ABSOLUTE | Constants.MOUSEEVENTF_VIRTUALDESK);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MOUSEINPUT Impl(int dx, int dy, uint mouseData, uint flags)
        {
            return new()
            {
                type = INPUT_TYPE.MOUSE,
                dx = dx,
                dy = dy,
                mouseData = mouseData,
                dwFlags = flags
            };
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public uint type;
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;

    public static class Create
    {
        private static class Constants
        {
            public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
            public const uint KEYEVENTF_KEYUP = 0x0002;
            public const uint KEYEVENTF_UNICODE = 0x0004;
        }

        public static KEYBDINPUT KeyInput(ushort virtualKey, bool extended, bool up)
        {
            var flags = 0u;
            if (extended) flags |= Constants.KEYEVENTF_EXTENDEDKEY;
            if (up) flags |= Constants.KEYEVENTF_KEYUP;

            ushort scan = User32.MapVirtualKeyToToVirtualScanCode(virtualKey);
            return Impl(virtualKey, scan, flags);

        }

        public static KEYBDINPUT UnicodeInput(char c, bool up)
        {
            var flags = Constants.KEYEVENTF_UNICODE;
            if (up) flags |= Constants.KEYEVENTF_KEYUP;

            return Impl(0, c, flags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static KEYBDINPUT Impl(ushort wVk, ushort wScan, uint dwFlags) => new()
        {
            type = INPUT_TYPE.KEYBOARD,
            wVk = wVk,
            wScan = wScan,
            dwFlags = dwFlags
        };
    }
}

// Union sized to the largest member so Marshal.SizeOf<INPUT>() matches what
// SendInput expects (same layout as in WindowsUInputKeyboard).
[StructLayout(LayoutKind.Explicit)]
public struct INPUT
{

    [FieldOffset(0)]
    public MOUSEINPUT mi;
    [FieldOffset(0)]
    public KEYBDINPUT ki;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator INPUT(MOUSEINPUT mi) => new()
    {
        mi = mi
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator INPUT(KEYBDINPUT ki) => new()
    {
        ki = ki
    };
}

file static class INPUT_TYPE
{
    public const int KEYBOARD = 1;
    public const int MOUSE = 0;
}