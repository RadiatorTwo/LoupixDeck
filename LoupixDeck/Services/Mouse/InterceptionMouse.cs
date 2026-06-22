using LoupixDeck.Models.Macros;
using LoupixDeck.Native;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Services.Mouse;

/// <inheritdoc/>
/// </summary>
public sealed class InterceptionMouse : InterceptionBase, IVirtualMouse
{
    // InterceptionMouseState flags.
    private const ushort StateLeftDown = 0x001;
    private const ushort StateLeftUp = 0x002;
    private const ushort StateRightDown = 0x004;
    private const ushort StateRightUp = 0x008;
    private const ushort StateMiddleDown = 0x010;
    private const ushort StateMiddleUp = 0x020;
    private const ushort StateWheel = 0x400;

    // InterceptionMouseFlag values.
    private const ushort FlagMoveRelative = 0x000;
    private const ushort FlagMoveAbsolute = 0x001;
    private const ushort FlagVirtualDesktop = 0x002;

    // One wheel detent, same unit as Win32 WHEEL_DELTA.
    private const short WheelDelta = 120;

    // Virtual-key codes of the physical mouse buttons (GetAsyncKeyState reports physical
    // buttons, matching the driver-level strokes we inject — button swap happens above us).
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    protected override int DeviceType => InterceptionDeviceType.Mouse;

    public void Click(MouseButton button)
    {
        var (down, up, vk) = ButtonStates(button);
        lock (_lock)
        {
            if (!EnsureContext()) return;
            SendButtonVerified(down, vk, expectDown: true);
            SendButtonVerified(up, vk, expectDown: false);
        }
    }

    public void ButtonDown(MouseButton button)
    {
        var (down, _, vk) = ButtonStates(button);
        lock (_lock)
        {
            if (!EnsureContext()) return;
            SendButtonVerified(down, vk, expectDown: true);
        }
    }

    public void ButtonUp(MouseButton button)
    {
        var (_, up, vk) = ButtonStates(button);
        lock (_lock)
        {
            if (!EnsureContext()) return;
            SendButtonVerified(up, vk, expectDown: false);
        }
    }

    public void MoveRelative(int dx, int dy)
    {
        lock (_lock)
        {
            if (!EnsureContext()) return;
            Send(new InterceptionMouseStroke() { Flags = FlagMoveRelative, X = dx, Y = dy });
            SpinFallbackPace();
        }
    }

    public void MoveAbsolute(int x, int y)
    {
        // Absolute coordinates are normalized to 0..65535 across the virtual desktop —
        // same convention as SendInput's MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
        var left = User32.SystemMetrics.VirtualScreenLeft;
        var top = User32.SystemMetrics.VirtualScreenTop;
        var width = User32.SystemMetrics.VirtualScreenWidth;
        var height = User32.SystemMetrics.VirtualScreenHeight;

        if (width <= 0 || height <= 0)
            return;

        var nx = (int)Math.Round((x - left) * 65535.0 / width);
        var ny = (int)Math.Round((y - top) * 65535.0 / height);

        lock (_lock)
        {
            if (!EnsureContext()) return;
            Send(new InterceptionMouseStroke()
                {
                    Flags = FlagMoveAbsolute | FlagVirtualDesktop,
                    X = nx,
                    Y = ny
                });
            SpinFallbackPace();
        }
    }

    public void Scroll(int amount)
    {
        lock (_lock)
        {
            if (!EnsureContext()) return;
            Send(new InterceptionMouseStroke()
                {
                    State = StateWheel,
                    Rolling = (short)(amount * WheelDelta)
                });
            SpinFallbackPace();
        }
    }

    // Sends a button stroke and waits until win32k's async key state reflects it — the same
    // handshake as InterceptionKeyboard.SendStrokeVerified: the driver reports strokes as
    // written even when the input stack drops them, so the button state actually changing is
    // the only reliable delivery signal. Moves and wheel events have no observable state and
    // use fixed pacing instead. Caller must hold _lock and have ensured the context exists.
    private void SendButtonVerified(ushort state, int vk, bool expectDown)
    {
        InterceptionMouseStroke stroke = new() { State = state };
        if (_ackFailures >= ConfigConstants.MaxConsecutiveAckFailures)
        {
            Send(stroke);
            SpinFallbackPace();
            return;
        }

        Send(stroke);

        if (WaitForKeyButtonState(vk, expectDown))
        {
            _ackFailures = 0;
        }
        else
        {
            // Not resent on purpose — a timeout can also mean "processed but not observable";
            // resending would duplicate the click.
            _ackFailures++;
            Console.Error.WriteLine(
                $"[InterceptionMouse] Button stroke 0x{state:X3} not acknowledged within {ConfigConstants.StrokeAckTimeoutMs} ms.");
        }
    }

    private static (ushort down, ushort up, int vk) ButtonStates(MouseButton button) => button switch
    {
        MouseButton.Right => (StateRightDown, StateRightUp, VkRButton),
        MouseButton.Middle => (StateMiddleDown, StateMiddleUp, VkMButton),
        _ => (StateLeftDown, StateLeftUp, VkLButton)
    };
}
