using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Records keyboard input on Windows via a low-level keyboard hook (WH_KEYBOARD_LL).
/// The hook runs on a dedicated thread with its own message pump and observes input
/// without consuming it (CallNextHookEx is always called), so keystrokes still reach
/// the focused application while recording. Auto-repeat down events are collapsed.
/// </summary>
public sealed class WindowsInputRecorder : IInputRecorder
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const uint WM_QUIT = 0x0012;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly object _gate = new();
    private readonly HashSet<int> _pressed = [];
    private readonly Stopwatch _stopwatch = new();

    private Thread _thread;
    private uint _threadId;
    private IntPtr _hookHandle;
    private LowLevelKeyboardProc _proc; // kept alive for the hook's lifetime
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
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _stopwatch.Stop();
    }

    private void HookThread(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        ready.Set();

        if (_hookHandle == IntPtr.Zero)
            return;

        // Standard message pump; GetMessage blocks until WM_QUIT is posted by Stop().
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _proc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
            TryRecord((int)wParam, Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam));

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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

    // ───────── Win32 interop ─────────

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
