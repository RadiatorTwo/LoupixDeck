#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.ActiveWindow;

/// <summary>
/// Tracks the foreground window via a <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>
/// out-of-context hook. The native callback is delivered on the thread that set the
/// hook and only while that thread pumps a Win32 message loop — so
/// <see cref="StartMonitoring"/> must be called from the UI thread (Avalonia pumps
/// Win32 messages there). The delegate and hook handle are kept as fields: if the
/// delegate were collected the native side would call into freed memory and crash.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsActiveWindowMonitor : IActiveWindowMonitor, IDisposable
{
    public event EventHandler<ActiveWindowInfo> ActiveWindowChanged;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    // Held as a field so the GC cannot collect the delegate the native hook calls back into.
    private WinEventDelegate _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _started;
    private (string Process, string Title) _last;

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        IntPtr lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(IntPtr hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;

        _proc = OnForegroundChanged;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            Marshal.GetFunctionPointerForDelegate(_proc), 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Native callback — must never let an exception unwind into native code.
        try
        {
            // Only the window object itself (not a child control / caret), and a real handle.
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;

            var processName = ResolveProcessName(hwnd);
            var title = ResolveTitle(hwnd);

            if (_last.Process == processName && _last.Title == title) return;
            _last = (processName, title);

            ActiveWindowChanged?.Invoke(this, new ActiveWindowInfo
            {
                ProcessName = processName,
                Title = title
            });
        }
        catch
        {
            /* ignore — never crash the native hook */
        }
    }

    private static string ResolveProcessName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName; // already without path or ".exe"
        }
        catch
        {
            // Protected / exited process, or pid no longer valid.
            return null;
        }
    }

    private static string ResolveTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;
        Span<char> buffer = length <= 256 ? stackalloc char[length + 1] : new char[length + 1];
        int written;
        unsafe
        {
            fixed (char* ptr = buffer)
                written = GetWindowText(hwnd, ptr, buffer.Length);
        }
        return written > 0 ? new string(buffer.Slice(0, written)) : string.Empty;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
#endif
