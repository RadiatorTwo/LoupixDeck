using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Windows implementation backed by the Interception kernel driver (interception.dll).
/// Unlike <see cref="WindowsVirtualMouse"/> (SendInput), the injected mouse events enter
/// the input stream below the user-mode layer, so apps that read raw input (games /
/// anti-cheat) receive them like a real mouse — same rationale as
/// <see cref="Services.InterceptionKeyboard"/>.
///
/// The DLL is not bundled — it is placed next to the executable by
/// <see cref="Services.InterceptionService"/> when the user installs the driver. If the DLL
/// is missing or the driver is not loaded, this backend reports itself unavailable and the
/// router falls back to SendInput.
/// </summary>
public class InterceptionMouse : IVirtualMouse
{
    // INTERCEPTION_MOUSE(0): keyboards are devices 1..10, mice 11..20.
    private const int MouseDevice = 11;

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

    // Send() gives up when the driver accepts no strokes for this long (see InterceptionKeyboard).
    private const int MaxStallMs = 500;

    // How long to wait for win32k to reflect an injected button stroke in the async key state.
    private const int StrokeAckTimeoutMs = 25;

    // After this many consecutive ack timeouts, button verification is considered unreliable
    // and injection falls back to fixed pacing.
    private const int MaxConsecutiveAckFailures = 3;

    // Pacing used for strokes that cannot be verified (moves, wheel) or when verification
    // became unreliable. Spin-based for sub-millisecond accuracy.
    private const double FallbackPaceMs = 2.0;

    // Virtual-key codes of the physical mouse buttons (GetAsyncKeyState reports physical
    // buttons, matching the driver-level strokes we inject — button swap happens above us).
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;

    // Virtual screen metrics (multi-monitor desktop bounding box) for absolute moves.
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(IntPtr context);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_send(IntPtr context, int device,
        [In] InterceptionStroke[] stroke, uint nstroke);

    // Native InterceptionMouseStroke layout:
    //   ushort state; ushort flags; short rolling; int x; int y; uint information;
    // C alignment puts x at offset 8 (after 2 bytes padding behind rolling) → size 20,
    // which is also the size of the generic InterceptionStroke blob used by the keyboard.
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InterceptionStroke
    {
        [FieldOffset(0)] public ushort State;
        [FieldOffset(2)] public ushort Flags;
        [FieldOffset(4)] public short Rolling;
        [FieldOffset(8)] public int X;
        [FieldOffset(12)] public int Y;
        [FieldOffset(16)] public uint Information;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly Lock _lock = new();

    private IntPtr _context = IntPtr.Zero;
    private bool? _available;
    private int _ackFailures;

    public bool Connected
    {
        get
        {
            lock (_lock)
            {
                return EnsureContext();
            }
        }
    }

    /// <summary>
    /// True when interception.dll is present and the driver is loaded. Cached for the
    /// process lifetime (installing the driver requires a reboot anyway).
    /// </summary>
    public bool IsDriverAvailable()
    {
        lock (_lock)
        {
            return EnsureContext();
        }
    }

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
            Send([new InterceptionStroke { Flags = FlagMoveRelative, X = dx, Y = dy }]);
            SpinDelay(FallbackPaceMs);
        }
    }

    public void MoveAbsolute(int x, int y)
    {
        // Absolute coordinates are normalized to 0..65535 across the virtual desktop —
        // same convention as SendInput's MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
            return;

        var nx = (int)Math.Round((x - left) * 65535.0 / width);
        var ny = (int)Math.Round((y - top) * 65535.0 / height);

        lock (_lock)
        {
            if (!EnsureContext()) return;
            Send([
                new InterceptionStroke
                {
                    Flags = FlagMoveAbsolute | FlagVirtualDesktop,
                    X = nx,
                    Y = ny
                }
            ]);
            SpinDelay(FallbackPaceMs);
        }
    }

    public void Scroll(int amount)
    {
        lock (_lock)
        {
            if (!EnsureContext()) return;
            Send([
                new InterceptionStroke
                {
                    State = StateWheel,
                    Rolling = (short)(amount * WheelDelta)
                }
            ]);
            SpinDelay(FallbackPaceMs);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_context != IntPtr.Zero)
            {
                try { interception_destroy_context(_context); }
                catch { /* DLL may be gone — nothing to clean up */ }
                _context = IntPtr.Zero;
            }
        }
    }

    // Lazily creates the interception context. Returns false (and caches it) when the DLL is
    // missing or the driver is not loaded. Caller must hold _lock.
    private bool EnsureContext()
    {
        if (_available.HasValue)
            return _available.Value && _context != IntPtr.Zero;

        try
        {
            _context = interception_create_context();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                      or EntryPointNotFoundException)
        {
            _context = IntPtr.Zero;
        }

        _available = _context != IntPtr.Zero;
        return _available.Value;
    }

    // Sends a button stroke and waits until win32k's async key state reflects it — the same
    // handshake as InterceptionKeyboard.SendStrokeVerified: the driver reports strokes as
    // written even when the input stack drops them, so the button state actually changing is
    // the only reliable delivery signal. Moves and wheel events have no observable state and
    // use fixed pacing instead. Caller must hold _lock and have ensured the context exists.
    private void SendButtonVerified(ushort state, int vk, bool expectDown)
    {
        if (_ackFailures >= MaxConsecutiveAckFailures)
        {
            Send([new InterceptionStroke { State = state }]);
            SpinDelay(FallbackPaceMs);
            return;
        }

        Send([new InterceptionStroke { State = state }]);

        if (WaitForButtonState(vk, expectDown))
        {
            _ackFailures = 0;
        }
        else
        {
            // Not resent on purpose — a timeout can also mean "processed but not observable";
            // resending would duplicate the click.
            _ackFailures++;
            Console.Error.WriteLine(
                $"[InterceptionMouse] Button stroke 0x{state:X3} not acknowledged within {StrokeAckTimeoutMs} ms.");
        }
    }

    // Polls the async key state until it matches the expected up/down state or the
    // acknowledge timeout elapses. Spin/yield only — no Thread.Sleep (15 ms resolution).
    private static bool WaitForButtonState(int vk, bool expectDown)
    {
        var sw = Stopwatch.StartNew();
        var spinner = new SpinWait();

        while (sw.ElapsedMilliseconds < StrokeAckTimeoutMs)
        {
            var isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
            if (isDown == expectDown) return true;
            spinner.SpinOnce(-1); // -1: yield, but never escalate to Thread.Sleep(1)
        }

        return false;
    }

    // Accurate short delay via Stopwatch spinning (Thread.Sleep rounds up to ~15 ms).
    private static void SpinDelay(double milliseconds)
    {
        var sw = Stopwatch.StartNew();
        var spinner = new SpinWait();
        while (sw.Elapsed.TotalMilliseconds < milliseconds)
            spinner.SpinOnce(-1);
    }

    // Caller must hold _lock and have ensured the context exists. Pushes strokes to the
    // driver, re-offering any it did not accept until they are in or the driver stalls.
    private void Send(InterceptionStroke[] strokes)
    {
        try
        {
            var sent = 0;
            var spinner = new SpinWait();
            var stall = Stopwatch.StartNew();

            while (sent < strokes.Length)
            {
                var chunk = sent == 0 ? strokes : strokes[sent..];
                var accepted = interception_send(_context, MouseDevice, chunk, (uint)chunk.Length);

                if (accepted > 0)
                {
                    sent += accepted;
                    spinner.Reset();
                    stall.Restart();
                }
                else if (stall.ElapsedMilliseconds > MaxStallMs)
                {
                    Console.Error.WriteLine(
                        $"[InterceptionMouse] Dropped {strokes.Length - sent} strokes (driver queue stalled).");
                    return;
                }
                else
                {
                    spinner.SpinOnce();
                }
            }
        }
        catch { /* swallow — backend stays best-effort, router covers fallback */ }
    }

    private static (ushort down, ushort up, int vk) ButtonStates(MouseButton button) => button switch
    {
        MouseButton.Right => (StateRightDown, StateRightUp, VkRButton),
        MouseButton.Middle => (StateMiddleDown, StateMiddleUp, VkMButton),
        _ => (StateLeftDown, StateLeftUp, VkLButton)
    };
}
