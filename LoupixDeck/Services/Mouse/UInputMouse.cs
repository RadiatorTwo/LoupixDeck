using System.Runtime.InteropServices;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Linux implementation backed by a uinput virtual mouse device (relative axes +
/// buttons + wheel). Same P/Invoke pattern as <see cref="UInputKeyboard"/>.
/// Absolute positioning is not supported (would require an EV_ABS device) — see
/// <see cref="MoveAbsolute"/>.
/// </summary>
public class UInputMouse : IVirtualMouse
{
    private const string UINPUT_PATH = "/dev/uinput";

    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;

    private const int UI_SET_EVBIT = 0x40045564;
    private const int UI_SET_KEYBIT = 0x40045565;
    private const int UI_SET_RELBIT = 0x40045566;

    private const int UI_DEV_CREATE = 0x5501;
    private const int UI_DEV_DESTROY = 0x5502;

    private const int EV_SYN = 0x00;
    private const int EV_KEY = 0x01;
    private const int EV_REL = 0x02;

    private const int SYN_REPORT = 0;

    private const int BTN_LEFT = 0x110;
    private const int BTN_RIGHT = 0x111;
    private const int BTN_MIDDLE = 0x112;

    private const int REL_X = 0x00;
    private const int REL_Y = 0x01;
    private const int REL_WHEEL = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public TimeVal time;
        public ushort type;
        public ushort code;
        public int value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeVal
    {
        public long tv_sec;
        public long tv_usec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UinputUserDev
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string name;
        public ushort id_bustype;
        public ushort id_vendor;
        public ushort id_product;
        public ushort id_version;
        public int ff_effects_max;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public int[] absmax;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public int[] absmin;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public int[] absfuzz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public int[] absflat;
    }

    private struct SsizeT(IntPtr value)
    {
        public IntPtr Value = value;
    }

    private struct SizeT(int v)
    {
        public IntPtr Value = v;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int fd, int request, int value);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern SsizeT write(int fd, IntPtr buffer, SizeT count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

    private int _fileDescriptor;
    private IntPtr _devPtr;
    private bool _disposed;

    public bool Connected { get; private set; }

    public UInputMouse()
    {
        try
        {
            _fileDescriptor = open(UINPUT_PATH, O_WRONLY | O_NONBLOCK);
        }
        catch (Exception)
        {
            Connected = false;
            return;
        }

        if (_fileDescriptor < 0)
        {
            // Same policy as UInputKeyboard: no exception, just report unavailable.
            Connected = false;
            return;
        }

        // Buttons
        ioctl(_fileDescriptor, UI_SET_EVBIT, EV_KEY);
        ioctl(_fileDescriptor, UI_SET_KEYBIT, BTN_LEFT);
        ioctl(_fileDescriptor, UI_SET_KEYBIT, BTN_RIGHT);
        ioctl(_fileDescriptor, UI_SET_KEYBIT, BTN_MIDDLE);

        // Relative axes + wheel
        ioctl(_fileDescriptor, UI_SET_EVBIT, EV_REL);
        ioctl(_fileDescriptor, UI_SET_RELBIT, REL_X);
        ioctl(_fileDescriptor, UI_SET_RELBIT, REL_Y);
        ioctl(_fileDescriptor, UI_SET_RELBIT, REL_WHEEL);

        var dev = new UinputUserDev
        {
            name = "LoupixVirtualMouse",
            id_bustype = 0,
            id_vendor = 0x1234,
            id_product = 0x5679,
            id_version = 1,
            absmax = new int[64],
            absmin = new int[64],
            absfuzz = new int[64],
            absflat = new int[64]
        };

        _devPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dev));
        Marshal.StructureToPtr(dev, _devPtr, false);

        write(_fileDescriptor, _devPtr, new SizeT(Marshal.SizeOf(dev)));
        ioctl(_fileDescriptor, UI_DEV_CREATE, 0);

        Connected = true;
    }

    public void Click(MouseButton button)
    {
        if (!Connected) return;

        var code = ButtonCode(button);
        SendEvent(EV_KEY, code, 1);
        SendEvent(EV_KEY, code, 0);
    }

    public void ButtonDown(MouseButton button)
    {
        if (!Connected) return;

        SendEvent(EV_KEY, ButtonCode(button), 1);
    }

    public void ButtonUp(MouseButton button)
    {
        if (!Connected) return;

        SendEvent(EV_KEY, ButtonCode(button), 0);
    }

    public void MoveRelative(int dx, int dy)
    {
        if (!Connected) return;

        SendInputEvent(EV_REL, REL_X, dx);
        SendInputEvent(EV_REL, REL_Y, dy);
        SendInputEvent(EV_SYN, SYN_REPORT, 0);
    }

    public void MoveAbsolute(int x, int y)
    {
        // Would require an EV_ABS uinput device with ABS_X/ABS_Y absinfo — out of scope for v1.
        Console.Error.WriteLine("[UInputMouse] MoveAbsolute is not supported on Linux.");
    }

    public void Scroll(int amount)
    {
        if (!Connected) return;

        SendEvent(EV_REL, REL_WHEEL, amount);
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (Connected)
        {
            ioctl(_fileDescriptor, UI_DEV_DESTROY, 0);
            close(_fileDescriptor);
            _fileDescriptor = -1;
        }

        if (_devPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_devPtr);
            _devPtr = IntPtr.Zero;
        }

        _disposed = true;
    }

    private static int ButtonCode(MouseButton button) => button switch
    {
        MouseButton.Right => BTN_RIGHT,
        MouseButton.Middle => BTN_MIDDLE,
        _ => BTN_LEFT
    };

    // Sends one event followed by a SYN report.
    private void SendEvent(int type, int code, int value)
    {
        SendInputEvent(type, code, value);
        SendInputEvent(EV_SYN, SYN_REPORT, 0);
    }

    private void SendInputEvent(int type, int code, int value)
    {
        var inputEvent = new InputEvent
        {
            type = (ushort)type,
            code = (ushort)code,
            value = value
        };

        var size = Marshal.SizeOf(inputEvent);
        var ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(inputEvent, ptr, false);

        write(_fileDescriptor, ptr, new SizeT(size));

        Marshal.FreeHGlobal(ptr);
    }
}
