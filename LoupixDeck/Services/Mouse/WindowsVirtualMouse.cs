using System.Runtime.InteropServices;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Windows implementation backed by the Win32 <c>SendInput</c> API (user32.dll).
/// Injected mouse events enter the session input stream like a real mouse. Note:
/// they carry the injected flag, so raw-input apps may ignore them (same limit as
/// <see cref="WindowsUInputKeyboard"/>) — <see cref="InterceptionMouse"/> covers
/// that case; <see cref="WindowsMouseRouter"/> picks between the two per call.
/// </summary>
public class WindowsVirtualMouse : IVirtualMouse
{
    private const int INPUT_MOUSE = 0;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const int WHEEL_DELTA = 120;

    // Virtual screen metrics (multi-monitor desktop bounding box).
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Union sized to the largest member so Marshal.SizeOf<INPUT>() matches what
    // SendInput expects (same layout as in WindowsUInputKeyboard).
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // SendInput requires no setup, so the backend is always available on Windows.
    public bool Connected => true;

    public void Click(MouseButton button)
    {
        var (down, up) = ButtonFlags(button);
        Send([MouseInput(0, 0, 0, down), MouseInput(0, 0, 0, up)]);
    }

    public void ButtonDown(MouseButton button)
    {
        var (down, _) = ButtonFlags(button);
        Send([MouseInput(0, 0, 0, down)]);
    }

    public void ButtonUp(MouseButton button)
    {
        var (_, up) = ButtonFlags(button);
        Send([MouseInput(0, 0, 0, up)]);
    }

    public void MoveRelative(int dx, int dy)
    {
        Send([MouseInput(dx, dy, 0, MOUSEEVENTF_MOVE)]);
    }

    public void MoveAbsolute(int x, int y)
    {
        // Absolute coordinates are normalized to 0..65535 across the virtual desktop.
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
            return;

        var nx = (int)Math.Round((x - left) * 65535.0 / width);
        var ny = (int)Math.Round((y - top) * 65535.0 / height);

        Send([MouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK)]);
    }

    public void Scroll(int amount)
    {
        Send([MouseInput(0, 0, unchecked((uint)(amount * WHEEL_DELTA)), MOUSEEVENTF_WHEEL)]);
    }

    public void Dispose()
    {
        // Nothing to dispose — SendInput holds no resources.
    }

    private static (uint down, uint up) ButtonFlags(MouseButton button) => button switch
    {
        MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
        _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
    };

    private static INPUT MouseInput(int dx, int dy, uint mouseData, uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = flags
                }
            }
        };
    }

    private static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;

        SendInput((uint)inputs.Length, inputs, InputSize);
    }
}
