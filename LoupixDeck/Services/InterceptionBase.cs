#nullable enable
using LoupixDeck.Native;
using LoupixDeck.Native.Types.Windows;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LoupixDeck.Services;

/// <summary>
/// Windows implementation backed by the Interception kernel driver (interception.dll).
/// Unlike <see cref="WindowsUInputKeyboard"/> and <see cref="WindowsVirtualMouse"/> (SendInput), the injected keystrokes and mouse events enter
/// the input stream below the user-mode layer, so apps that read raw input (games /
/// anti-cheat) receive them like a real keyboard and mouse.
/// </summary>
/// <remarks>
/// The DLL is not bundled.
/// Access is exclusively through the official interception.dll API (the driver's licence
/// only permits library access, not opening the kernel device directly).
/// The DLL is not bundled — it is placed next to the executable by <see cref="InterceptionService"/> when
/// the user installs the driver. If the DLL is missing or the driver is not loaded, this
/// backend reports itself unavailable and the router falls back to SendInput.
/// </remarks>
public abstract class InterceptionBase : IDisposable
{
    private string ClassName => field ??= GetType().Name;

    protected static class ConfigConstants
    {
        // Send() gives up when the driver accepts no strokes for this long. A healthy input
        // stack drains the driver queue within microseconds, so a stall this long means the
        // stack is wedged and the remaining strokes would never get through.
        public const int MaxStallMs = 500;
        public const long MaxStallTicks = TimeSpan.TicksPerMillisecond * MaxStallMs;

        // How long to wait for win32k to reflect an injected stroke in the async key state.
        // Normal processing takes well under a millisecond; the timeout only fires when the
        // stroke was dropped or the scan-code → VK mapping does not match the active layout.
        public const int StrokeAckTimeoutMs = 25;
        public const long StrokeAckTimeoutTicks = TimeSpan.TicksPerMillisecond * StrokeAckTimeoutMs;

        // After this many consecutive ack timeouts, key-state/button verification is considered
        // unreliable (e.g. keyboard-layout mismatch) and injection falls back to fixed pacing.
        public const int MaxConsecutiveAckFailures = 3;

        // Pacing used when verification is unavailable or unreliable. Spin-based, so the real
        // duration is accurate (Thread.Sleep would round up to the ~15 ms timer resolution).
        public const double FallbackPaceMs = 2.0;
        public const long FallbackPaceTicks = (long)(TimeSpan.TicksPerMillisecond * FallbackPaceMs);
    }

    protected static class InterceptionDeviceType
    {
        public const int Keyboard = InterceptionContext.KeyboardDevice;
        public const int Mouse = InterceptionContext.MouseDevice;
    }

    private enum InterceptionKeyState : ushort
    {
        KeyDown = 0x00,
        KeyUp = 0x01,
        KeyE0 = 0x02,
    }

    protected readonly Lock _lock = new();
    protected abstract int DeviceType { get; }
    protected InterceptionContext? ctx;
    protected int _ackFailures;
    private bool disposedValue;

    public bool Connected => IsDriverAvailable();

    /// <summary>
    /// True when interception.dll is present and the driver is loaded (a context can be created).
    /// </summary>
    /// <remarks>
    /// The result is cached for the process lifetime: installing the driver
    /// requires a reboot, which restarts the app, so re-evaluation per call is pointless.
    /// <remarks>
    public bool IsDriverAvailable()
    {
        lock (_lock)
        {
            return EnsureContext();
        }
    }

    protected InterceptionBase() { }

    private bool? ensureContextCache;
    // Lazily creates the interception context. Returns false (and caches it) when the DLL is
    // missing or the driver is not loaded. Caller must hold _lock.
    [MemberNotNullWhen(true, nameof(ctx))]
    protected bool EnsureContext()
    {
        if (ensureContextCache is not null)
            return ensureContextCache.Value && ctx?.IsInvalid is false;
        else
            return SlowTail();
        bool SlowTail()
        {

            try
            {
                ctx = InterceptionContext.Create(DeviceType);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
                                          or EntryPointNotFoundException)
            {
                ctx = null;
            }

            return (ensureContextCache = ctx?.IsInvalid is false).Value;
        }
    }

    // Sends a single stroke and waits (spin, sub-millisecond granularity) until win32k's
    // async key state reflects it.
    // The interception driver reports strokes as written even
    // when the input stack silently drops them (queue overflow), so the button/key state actually changing is
    // the only reliable delivery signal.  (and at the same time the fastest possible pacing: injection proceeds the moment the previous stroke is through.)

    // Mouse moves and wheel events have no observable state and use fixed pacing instead.

    // Caller must hold _lock and have ensured the context exists.
    // private void SendButtonStrokeVerified(InterceptionStroke item, uint vk, bool expectDown, Action onFailureLog);

    protected void InjectWithDelay(InterceptionStroke item)
    {
        // Cannot (or should not) verify — inject and pace with a fixed spin delay.
        Send(item);
        SpinFallbackPace();
    }

    // Polls the async key state until it matches the expected up/down state or the
    // acknowledge timeout elapses. Spin/yield only — no Thread.Sleep (15 ms resolution).
    protected static bool WaitForKeyButtonState(int vk, bool expectDown)
    {
        Stopwatch sw = Stopwatch.StartNew();
        SpinWait spinner = new();

        while (sw.ElapsedTicks < ConfigConstants.StrokeAckTimeoutTicks)
        {
            bool isDown = User32.AsyncKeyStateIsDown(vk);
            if (isDown == expectDown) return true;
            spinner.SpinOnce(-1); // -1: yield, but never escalate to Thread.Sleep(1)
        }

        return false;
    }

    // Accurate short delay via Stopwatch spinning.
    // Thread.Sleep is unusable for pacing on
    // Windows because its real resolution is the ~15 ms system timer tick.
    // private static void SpinDelay(double milliseconds) => SpinDelay(TimeSpan.FromMilliseconds(milliseconds));
    // private static void SpinDelay(TimeSpan duration) => SpinDelay(duration.Ticks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void SpinFallbackPace() => SpinDelay(ConfigConstants.FallbackPaceTicks);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SpinDelay(long waitTicks)
    {
        Stopwatch sw = Stopwatch.StartNew();
        SpinWait spinner = new();
        while (sw.Elapsed.Ticks < waitTicks)
            spinner.SpinOnce(-1); // -1: yield, but never escalate to Thread.Sleep(1)
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void Send(in InterceptionStroke stroke) => Send(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in stroke), 1));

    // Caller must hold _lock and have ensured the context exists. Pushes strokes to the
    // driver, re-offering any it did not accept until they are in or the driver stalls.
    protected void Send(ReadOnlySpan<InterceptionStroke> strokes)
    {
        Debug.Assert(ctx is not null);
        if (strokes.Length is 1)
        {
            Send(strokes[0]);
            return;
        }

        try
        {
            var sent = 0;
            var spinner = new SpinWait();
            var stall = Stopwatch.StartNew();

            while (sent < strokes.Length)
            {
                var chunk = strokes.Slice(sent);
                var accepted = ctx.Send(chunk);

                if (accepted > 0)
                {
                    sent += accepted;
                    spinner.Reset();
                    stall.Restart();
                }
                else if (stall.ElapsedTicks > ConfigConstants.MaxStallTicks)
                {
                    Console.Error.WriteLine($"[{ClassName}] Dropped {strokes.Length - sent} strokes (driver queue stalled).");
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

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
            return;

        if (disposing)
        {
        }

        ctx?.Dispose();
        ctx = null;
        disposedValue = true;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~InterceptionBase()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}