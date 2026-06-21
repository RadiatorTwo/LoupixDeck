using System.Diagnostics;
using LoupixDeck.Models;
using LoupixDeck.Native;
using LoupixDeck.Native.Types.Windows;
using LoupixDeck.Utils;

namespace LoupixDeck.Services;

/// <summary>
/// Windows implementation backed by the Interception kernel driver (interception.dll).
/// Unlike <see cref="WindowsUInputKeyboard"/> (SendInput), the injected keystrokes enter
/// the input stream below the user-mode layer, so apps that read raw input (games /
/// anti-cheat) receive them like a real keyboard.
///
/// Access is exclusively through the official interception.dll API (the driver's licence
/// only permits library access, not opening the kernel device directly). The DLL is not
/// bundled — it is placed next to the executable by <see cref="InterceptionService"/> when
/// the user installs the driver. If the DLL is missing or the driver is not loaded, this
/// backend reports itself unavailable and the router falls back to SendInput.
/// </summary>
public sealed class InterceptionKeyboard : IUInputKeyboard
{
    // InterceptionKeyState flags. (Named State* to avoid colliding with the
    // IUInputKeyboard.KeyDown/KeyUp methods.)
    private const ushort StateKeyDown = 0x00;
    private const ushort StateKeyUp = 0x01;
    private const ushort KeyE0 = 0x02;

    // Left Shift make code (PS/2 set 1) — used to type shifted characters in SendText.
    private const int ScanLeftShift = 0x2A;

    // Send() gives up when the driver accepts no strokes for this long. A healthy input
    // stack drains the driver queue within microseconds, so a stall this long means the
    // stack is wedged and the remaining strokes would never get through.
    private const int MaxStallMs = 500;

    // MapVirtualKey translation type: scan code → virtual key, distinguishing left/right
    // modifier keys and honouring the extended-key (E0) prefix.
    private const uint MapVkVscToVkEx = 3;

    // How long to wait for win32k to reflect an injected stroke in the async key state.
    // Normal processing takes well under a millisecond; the timeout only fires when the
    // stroke was dropped or the scan-code → VK mapping does not match the active layout.
    private const int StrokeAckTimeoutMs = 25;

    // After this many consecutive ack timeouts, key-state verification is considered
    // unreliable (e.g. keyboard-layout mismatch) and injection falls back to fixed pacing.
    private const int MaxConsecutiveAckFailures = 3;

    // Pacing used when verification is unavailable or unreliable. Spin-based, so the real
    // duration is accurate (Thread.Sleep would round up to the ~15 ms timer resolution).
    private const double FallbackPaceMs = 2.0;

    private readonly Lock _lock = new();
    private readonly KeyboardLayout _layout;

    private InterceptionContext context = null;
    private bool? _available;
    private int _ackFailures;

    public bool Connected { get; set; }

    public InterceptionKeyboard()
    {
        _layout = KeyboardLayouts.GetLayout(GetCurrentKeyboardLayout());
    }

    /// <summary>
    /// True when interception.dll is present and the driver is loaded (a context can be
    /// created). The result is cached for the process lifetime: installing the driver
    /// requires a reboot, which restarts the app, so re-evaluation per call is pointless.
    /// </summary>
    public bool IsDriverAvailable()
    {
        lock (_lock)
        {
            return EnsureContext();
        }
    }

    public void SendKey(int keyCode)
    {
        // keyCode is a PS/2 set-1 scan code (non-extended) for interface compatibility.
        lock (_lock)
        {
            if (!EnsureContext()) return;
            SendStrokeVerified((ushort)keyCode, false, false);
            SendStrokeVerified((ushort)keyCode, false, true);
        }
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_lock)
        {
            if (!EnsureContext()) return;

            // One stroke at a time, each acknowledged via the async key state before the
            // next is injected. The driver gives no backpressure signal (it reports strokes
            // as written even when the stack above drops them), so this handshake is the
            // fastest loss-free rate possible.
            foreach (var c in text)
            {
                if (!_layout.KeyMap.TryGetValue(c, out var key)) continue;

                if (key.shift) SendStrokeVerified(ScanLeftShift, false, false);
                SendStrokeVerified((ushort)key.keycode, false, false);
                SendStrokeVerified((ushort)key.keycode, false, true);
                if (key.shift) SendStrokeVerified(ScanLeftShift, false, true);
            }
        }
    }

    public void SendKeyCombination(IReadOnlyList<string> keyNames)
    {
        if (keyNames == null || keyNames.Count == 0) return;

        var keys = new List<(int scanCode, bool e0)>(keyNames.Count);
        foreach (var name in keyNames)
        {
            if (KeyNames.TryGetInterception(name, out var scanCode, out var e0))
                keys.Add((scanCode, e0));
            else
                Console.Error.WriteLine($"[InterceptionKeyboard] Unknown key name: '{name}'");
        }

        if (keys.Count == 0) return;

        lock (_lock)
        {
            if (!EnsureContext()) return;

            // Press all in order, then release in reverse order. Each stroke is acknowledged
            // before the next, so modifier state is guaranteed when the main key arrives.
            foreach (var (scanCode, e0) in keys)
                SendStrokeVerified((ushort)scanCode, e0, false);
            for (var k = keys.Count - 1; k >= 0; k--)
                SendStrokeVerified((ushort)keys[k].scanCode, keys[k].e0, true);
        }
    }

    public void KeyDown(string keyName)
    {
        SendSingle(keyName, up: false);
    }

    public void KeyUp(string keyName)
    {
        SendSingle(keyName, up: true);
    }

    private void SendSingle(string keyName, bool up)
    {
        if (!KeyNames.TryGetInterception(keyName, out var scanCode, out var e0))
        {
            Console.Error.WriteLine($"[InterceptionKeyboard] Unknown key name: '{keyName}'");
            return;
        }

        lock (_lock)
        {
            if (!EnsureContext()) return;
            SendStrokeVerified((ushort)scanCode, e0, up);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            context?.Dispose();
            context = null;
        }
    }

    // Lazily creates the interception context. Returns false (and caches it) when the DLL is
    // missing or the driver is not loaded. Caller must hold _lock.
    private bool EnsureContext()
    {
        if (_available.HasValue)
            return _available.Value && context?.IsInvalid is false;
        
        context = InterceptionContext.CreateKeyboard();

        _available = context?.IsInvalid is false;
        Connected = _available.Value;
        return _available.Value;
    }

    // Sends a single stroke and waits (spin, sub-millisecond granularity) until win32k's
    // async key state reflects it. The interception driver reports strokes as written even
    // when the input stack above it silently drops them (queue overflow), so the key state
    // actually changing is the only reliable delivery signal — and at the same time the
    // fastest possible pacing: injection proceeds the moment the previous stroke is through.
    // Caller must hold _lock and have ensured the context exists.
    private void SendStrokeVerified(ushort code, bool e0, bool up)
    {
        // Resolve the VK that win32k will track for this scan code. 0 = no mapping.
        var vk = User32.MapVirtualScanCodeToVirtualKeyEx(e0 ? 0xE000u | code : code);

        if (vk == 0 || _ackFailures >= MaxConsecutiveAckFailures)
        {
            // Cannot (or should not) verify — inject and pace with a fixed spin delay.
            Send(Stroke(code, e0, up));
            SpinDelay(FallbackPaceMs);
            return;
        }

        Send(Stroke(code, e0, up));

        if (WaitForKeyState((int)vk, expectDown: !up))
        {
            _ackFailures = 0;
        }
        else
        {
            // Not resent on purpose: a timeout can also mean "processed but not observable"
            // (e.g. per-window layout mismatch) — resending would duplicate the keystroke.
            _ackFailures++;
            Console.Error.WriteLine(
                $"[InterceptionKeyboard] Stroke 0x{code:X2} ({(up ? "up" : "down")}) not acknowledged within {StrokeAckTimeoutMs} ms.");
        }
    }

    // Polls the async key state until it matches the expected up/down state or the
    // acknowledge timeout elapses. Spin/yield only — no Thread.Sleep (15 ms resolution).
    private static bool WaitForKeyState(int vk, bool expectDown)
    {
        var sw = Stopwatch.StartNew();
        var spinner = new SpinWait();

        while (sw.ElapsedMilliseconds < StrokeAckTimeoutMs)
        {
            var isDown = (User32.GetAsyncKeyState(vk) & 0x8000) != 0;
            if (isDown == expectDown) return true;
            spinner.SpinOnce(-1); // -1: yield, but never escalate to Thread.Sleep(1)
        }

        return false;
    }

    // Accurate short delay via Stopwatch spinning. Thread.Sleep is unusable for pacing on
    // Windows because its real resolution is the ~15 ms system timer tick.
    private static void SpinDelay(double milliseconds)
    {
        var sw = Stopwatch.StartNew();
        var spinner = new SpinWait();
        while (sw.Elapsed.TotalMilliseconds < milliseconds)
            spinner.SpinOnce(-1);
    }

    // Caller must hold _lock and have ensured the context exists. Pushes strokes to the
    // driver, re-offering any it did not accept until they are in or the driver stalls.
    private void Send(ReadOnlySpan<InterceptionKeyStroke> strokes)
    {
        if(strokes.Length is 1)
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
                var chunk = sent == 0 ? strokes : strokes[sent..];
                var accepted = context.Send(chunk);

                if (accepted > 0)
                {
                    sent += accepted;
                    spinner.Reset();
                    stall.Restart();
                }
                else if (stall.ElapsedMilliseconds > MaxStallMs)
                {
                    Console.Error.WriteLine(
                        $"[InterceptionKeyboard] Dropped {strokes.Length - sent} strokes (driver queue stalled).");
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
    private void Send(InterceptionKeyStroke stroke)
    {
        try
        {
            var sent = 0;
            var spinner = new SpinWait();
            var stall = Stopwatch.StartNew();

            var accepted = context.Send(stroke);

            if (accepted > 0)
            {
                sent += accepted;
                spinner.Reset();
                stall.Restart();
            }
            else if (stall.ElapsedMilliseconds > MaxStallMs)
            {
                Console.Error.WriteLine(
                    $"[InterceptionKeyboard] Dropped one stroke (driver queue stalled).");
                return;
            }
            else
            {
                spinner.SpinOnce();
            }
        }
        catch { /* swallow — backend stays best-effort, router covers fallback */ }
    }

    private static InterceptionKeyStroke Stroke(ushort code, bool e0, bool up)
    {
        ushort state = up ? StateKeyUp : StateKeyDown;
        if (e0) state |= KeyE0;
        return new() { Code = code, State = state, Information = 0 };
    }

    private static string GetCurrentKeyboardLayout()
    {
        try
        {
            // Low word of the HKL is the LANGID; primary language id lives in its low 10 bits.
            var primary = User32.GetPrimaryKeyboardLayoutLanguageId(0);
            return primary == 0x07 ? "de" : "us"; // 0x07 = German, fall back to US otherwise
        }
        catch
        {
            return "us";
        }
    }
}
