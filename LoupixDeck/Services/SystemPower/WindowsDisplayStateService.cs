#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Follows <c>GUID_CONSOLE_DISPLAY_STATE</c> through <c>PowerSettingRegisterNotification</c> with a
/// callback recipient, so no window and no message pump are involved. Windows delivers the current
/// state right after registration and then every change: 0 = off, 1 = on, 2 = dimmed. Dimmed still
/// shows a picture and counts as on.
///
/// A running Windows screen saver counts as "displays off" too, although the monitors stay powered:
/// to the user the computer has gone idle the same way. Two cases, both seen through WinEvents:
/// <list type="bullet">
/// <item>Started by the idle timeout, it runs on a desktop of its own, so its start and end are each
/// a desktop switch, answered with a one-off <c>SPI_GETSCREENSAVERRUNNING</c> query.</item>
/// <item>Started by "Preview" (or directly), it runs on the user's desktop and takes the foreground;
/// that is followed until its <c>.scr</c> process exits.</item>
/// </list>
/// The hooks live on a thread of their own with its own message loop, because out-of-context
/// WinEvents only arrive on the hooking thread while it pumps.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsDisplayStateService : DisplayStateServiceBase, IDisposable
{
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private const uint DEVICE_NOTIFY_CALLBACK = 2;
    private const uint PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint ERROR_SUCCESS = 0;
    private const uint DisplayOff = 0;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;
    private const uint WM_QUIT = 0x0012;

    // Offsets in POWERBROADCAST_SETTING: GUID PowerSetting, DWORD DataLength, UCHAR Data[].
    private const int DataLengthOffset = 16;
    private const int DataOffset = 20;

    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    private readonly Lock _stateGate = new();
    private bool _monitorsOn = true;
    private bool _screenSaverDesktop;
    private Process _screenSaverProcess;

    // Held as fields so the GC cannot collect the delegates native code calls back into.
    private DeviceNotifyCallbackRoutine _callback;
    private WinEventDelegate _winEventProc;
    private PowerSettingNotificationHandle _registration;
    private Thread _hookThread;
    private uint _hookThreadId;

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(in Guid settingGuid, uint flags,
        in DeviceNotifySubscribeParameters recipient, out PowerSettingNotificationHandle registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(IntPtr registrationHandle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        IntPtr lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    // The W variant: SPI_GETSCREENSAVERRUNNING passes no string, so A and W behave the same.
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint uiAction, uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool pvParam, uint fWinIni);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(in Msg lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    protected override void Start()
    {
        _callback = OnPowerSettingChanged;
        DeviceNotifySubscribeParameters recipient = new()
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = IntPtr.Zero
        };

        uint result = PowerSettingRegisterNotification(ConsoleDisplayState, DEVICE_NOTIFY_CALLBACK,
            recipient, out _registration);
        if (result != ERROR_SUCCESS)
        {
            _registration.Dispose();
            _registration = null;
            Console.WriteLine($"[Displays] PowerSettingRegisterNotification failed: {result}");
            return;
        }

        _winEventProc = OnWinEvent;
        _hookThread = new Thread(RunHookThread) { IsBackground = true, Name = "Screen saver hook" };
        _hookThread.Start();

        MarkSupported();
    }

    /// <summary>Sets the WinEvent hooks and pumps messages for them until <see cref="Dispose"/>.</summary>
    private void RunHookThread()
    {
        _hookThreadId = GetCurrentThreadId();
        IntPtr proc = Marshal.GetFunctionPointerForDelegate(_winEventProc);
        IntPtr desktopHook = SetWinEventHook(EVENT_SYSTEM_DESKTOPSWITCH, EVENT_SYSTEM_DESKTOPSWITCH,
            IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        IntPtr foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (desktopHook == IntPtr.Zero || foregroundHook == IntPtr.Zero)
            Console.WriteLine("[Displays] WinEvent hook unavailable; the screen saver may not be followed.");

        try
        {
            // GetMessage returns 0 on WM_QUIT and -1 on error; both end the loop.
            while (GetMessage(out Msg msg, IntPtr.Zero, 0, 0) > 0)
                DispatchMessage(msg);
        }
        finally
        {
            if (desktopHook != IntPtr.Zero) UnhookWinEvent(desktopHook);
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        }
    }

    private uint OnPowerSettingChanged(IntPtr context, uint type, IntPtr setting)
    {
        // Native callback - an exception must never unwind into the power manager.
        try
        {
            if (type != PBT_POWERSETTINGCHANGE || setting == IntPtr.Zero) return ERROR_SUCCESS;
            if (Marshal.PtrToStructure<Guid>(setting) != ConsoleDisplayState) return ERROR_SUCCESS;
            if (Marshal.ReadInt32(setting, DataLengthOffset) < sizeof(uint)) return ERROR_SUCCESS;

            uint state = (uint)Marshal.ReadInt32(setting, DataOffset);
            lock (_stateGate)
            {
                _monitorsOn = state != DisplayOff;
                Publish();
            }
        }
        catch
        {
            /* ignore - never crash the native callback */
        }

        return ERROR_SUCCESS;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Native callback - an exception must never unwind into the hook.
        try
        {
            if (eventType == EVENT_SYSTEM_DESKTOPSWITCH)
                OnDesktopSwitched();
            else if (eventType == EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero && idObject == 0 && idChild == 0)
                OnForegroundChanged(hwnd);
        }
        catch
        {
            /* ignore - never crash the native hook */
        }
    }

    private void OnDesktopSwitched()
    {
        if (!SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, out bool running, 0)) return;
        lock (_stateGate)
        {
            _screenSaverDesktop = running;
            Publish();
        }
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return;

        Process process = Process.GetProcessById((int)pid);
        if (!IsScreenSaverProcess(process))
        {
            process.Dispose();
            return;
        }

        lock (_stateGate)
        {
            if (_screenSaverProcess?.Id == process.Id)
            {
                process.Dispose();
                return;
            }

            _screenSaverProcess?.Dispose();
            _screenSaverProcess = process;
        }

        // A screen saver ends by exiting, whatever the user does to stop it.
        process.EnableRaisingEvents = true;
        process.Exited += OnScreenSaverExited;

        lock (_stateGate)
        {
            if (process.HasExited)
            {
                ForgetScreenSaver(process);
                return;
            }

            Publish();
        }
    }

    private static bool IsScreenSaverProcess(Process process)
    {
        try
        {
            string path = process.MainModule?.FileName;
            return path != null && path.EndsWith(".scr", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Protected or already gone; a screen saver runs as the user and is neither.
            return false;
        }
    }

    private void OnScreenSaverExited(object sender, EventArgs e)
    {
        lock (_stateGate) ForgetScreenSaver((Process)sender);
    }

    /// <summary>Drops the followed screen saver process if it is still the current one. Caller holds the lock.</summary>
    private void ForgetScreenSaver(Process process)
    {
        if (!ReferenceEquals(process, _screenSaverProcess)) return;
        _screenSaverProcess.Dispose();
        _screenSaverProcess = null;
        Publish();
    }

    /// <summary>
    /// The displays count as on while the monitors are powered and no screen saver runs. Caller
    /// holds the lock, so callbacks on different threads cannot overtake each other; the
    /// subscribers only post to the UI thread.
    /// </summary>
    private void Publish() => Report(_monitorsOn && !_screenSaverDesktop && _screenSaverProcess == null);

    public void Dispose()
    {
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _registration?.Dispose();
        _registration = null;

        lock (_stateGate)
        {
            _screenSaverProcess?.Dispose();
            _screenSaverProcess = null;
        }
    }

    /// <summary>Owns an <c>HPOWERNOTIFY</c> and unregisters it on release.</summary>
    private sealed class PowerSettingNotificationHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => PowerSettingUnregisterNotification(handle) == ERROR_SUCCESS;
    }
}
#endif
