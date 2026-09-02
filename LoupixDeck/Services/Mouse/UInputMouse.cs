using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Linux implementation backed by a uinput virtual mouse device (relative axes +
/// buttons + wheel). Same P/Invoke pattern as <see cref="UInputKeyboard"/>.
/// Absolute positioning is not supported (would require an EV_ABS device) — see
/// <see cref="MoveAbsolute"/>.
/// </summary>
public partial class UInputMouse : IVirtualMouse
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

    // linux-x64 input_event: timeval (16) + type/code (4) + value (4).
    private const int InputEventSize = 24;

    // uinput_user_dev: name[80] + input_id (8) + ff_effects_max (4) + 4 * abs[64].
    private const int UinputMaxNameSize = 80;
    private const int AbsCnt = 64;
    private const int UinputUserDevSize = UinputMaxNameSize + 8 + 4 + (AbsCnt * 4 * 4);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int ioctl(int fd, int request, int value);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint write(int fd, ReadOnlySpan<byte> buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int close(int fd);

    private int _fileDescriptor;
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

        WriteUserDev("LoupixVirtualMouse", vendor: 0x1234, product: 0x5679);
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
        Span<byte> buffer = stackalloc byte[InputEventSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(16), (ushort)type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(18), (ushort)code);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20), value);
        write(_fileDescriptor, buffer, (nuint)InputEventSize);
    }

    private void WriteUserDev(string name, ushort vendor, ushort product)
    {
        Span<byte> buffer = stackalloc byte[UinputUserDevSize];
        buffer.Clear();
        Encoding.ASCII.GetBytes(name, buffer.Slice(0, UinputMaxNameSize - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(82), vendor);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(84), product);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(86), 1);
        write(_fileDescriptor, buffer, (nuint)UinputUserDevSize);
    }
}
