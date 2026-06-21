using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Native;
using LoupixDeck.Native.Types.Windows;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Records keyboard input on Windows via a low-level keyboard hook (WH_KEYBOARD_LL).
/// The hook runs on a dedicated thread with its own message pump and observes input
/// without consuming it (CallNextHookEx is always called), so keystrokes still reach
/// the focused application while recording. Auto-repeat down events are collapsed.
/// </summary>
public sealed partial class WindowsInputRecorder : IInputRecorder
{
    private const uint WM_QUIT = 0x0012;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly Lock _gate = new();
    private readonly HashSet<int> _pressed = [];
    private readonly Stopwatch _stopwatch = new();

    private Thread _thread;
    private uint _threadId;
    private WindowsHook hook;
    private TimeSpan _lastEventAt;
    private bool _hasLastEvent;

    public bool IsSupported => true;
    public bool IsRecording { get; private set; }

    public event EventHandler<RecordedKeyEventArgs> KeyRecorded;

    public void Start()
    {
        if (IsRecording)
            return;

        IsRecording = true;
        _pressed.Clear();
        _hasLastEvent = false;
        _lastEventAt = TimeSpan.Zero;
        _stopwatch.Restart();

        var ready = new ManualResetEventSlim(false);

        _thread = new Thread(() => HookThread(ready))
        {
            IsBackground = true,
            Name = "LoupixDeck.InputRecorder"
        };
        _thread.Start();

        // Wait until the hook thread has installed the hook and captured its thread id.
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (!IsRecording)
            return;

        IsRecording = false;

        if (_threadId != 0)
            User32.Messages.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _stopwatch.Stop();
    }

    private void HookThread(ManualResetEventSlim ready)
    {
        _threadId = User32.Threads.GetCurrentThreadId();
        try
        {
            hook = new(WindowsHookType.WH_KEYBOARD_LL, HookCallback)
            {
                HookCodeFilter = WindowsHook.HookCodes.HC_ACTION
            };
        }
        catch (Exception)
        {
            hook = null;
        }
        ready.Set();

        if (hook?.IsInvalid is not false)
        {
            hook?.Close();
            hook = null;
            return;
        }

        // Standard message pump; GetMessage blocks until WM_QUIT is posted by Stop().
        while (User32.Messages.TryGetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            User32.Messages.TranslateMessage(in msg);
            User32.Messages.DispatchMessage(in msg);
        }

        hook?.Close();
        hook = null;

        void HookCallback(int nCode, IntPtr wParam, IntPtr lParam) => TryRecord(unchecked((int)wParam), Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam));
    }

    private void TryRecord(int message, KBDLLHOOKSTRUCT data)
    {
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var isUp = message is WM_KEYUP or WM_SYSKEYUP;
        if (!isDown && !isUp)
            return;

        var vk = (int)data.vkCode;
        if (!WindowsVkNames.TryGetName(vk, out var name))
            return;

        RecordedKeyEventArgs args;
        lock (_gate)
        {
            if (isDown)
            {
                // Collapse OS auto-repeat: only the first press of a held key is recorded.
                if (!_pressed.Add(vk))
                    return;
            }
            else
            {
                _pressed.Remove(vk);
            }

            var now = _stopwatch.Elapsed;
            var sinceLast = _hasLastEvent ? now - _lastEventAt : TimeSpan.Zero;
            _lastEventAt = now;
            _hasLastEvent = true;

            args = new RecordedKeyEventArgs(name, isDown, sinceLast);
        }

        KeyRecorded?.Invoke(this, args);
    }
}
