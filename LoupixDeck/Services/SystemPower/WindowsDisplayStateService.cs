#if WINDOWS
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
/// to the user the computer has gone idle the same way. Windows runs it on a desktop of its own, so
/// its start and end are each a desktop switch; the WinEvent for that switch triggers a one-off
/// <c>SPI_GETSCREENSAVERRUNNING</c> query.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsDisplayStateService : DisplayStateServiceBase, IDisposable
{
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private const uint DEVICE_NOTIFY_CALLBACK = 2;
    private const uint PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint ERROR_SUCCESS = 0;
    private const uint DisplayOff = 0;

    private const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint SPI_GETSCREENSAVERRUNNING = 0x0072;

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

    private readonly Lock _stateGate = new();
    private bool _monitorsOn = true;
    private bool _screenSaverRunning;

    // Held as fields so the GC cannot collect the delegates native code calls back into.
    private DeviceNotifyCallbackRoutine _callback;
    private WinEventDelegate _desktopSwitchProc;
    private PowerSettingNotificationHandle _registration;
    private IntPtr _desktopSwitchHook = IntPtr.Zero;

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

        // Out-of-context WinEvent callbacks arrive on the thread that set the hook while it pumps
        // messages; StartMonitoring runs on the UI thread, which Avalonia pumps.
        _desktopSwitchProc = OnDesktopSwitched;
        _desktopSwitchHook = SetWinEventHook(EVENT_SYSTEM_DESKTOPSWITCH, EVENT_SYSTEM_DESKTOPSWITCH, IntPtr.Zero,
            Marshal.GetFunctionPointerForDelegate(_desktopSwitchProc), 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_desktopSwitchHook == IntPtr.Zero)
            Console.WriteLine("[Displays] Desktop switch hook unavailable; the screen saver is not followed.");

        MarkSupported();
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
            lock (_stateGate) _monitorsOn = state != DisplayOff;
            Publish();
        }
        catch
        {
            /* ignore - never crash the native callback */
        }

        return ERROR_SUCCESS;
    }

    private void OnDesktopSwitched(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Native callback - an exception must never unwind into the hook.
        try
        {
            if (!SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, out bool running, 0)) return;
            lock (_stateGate) _screenSaverRunning = running;
            Publish();
        }
        catch
        {
            /* ignore - never crash the native hook */
        }
    }

    /// <summary>The displays count as on while the monitors are powered and no screen saver runs.</summary>
    private void Publish()
    {
        // Reported under the lock so a power callback and a screen saver exit on two threads
        // cannot overtake each other; the subscribers only post to the UI thread.
        lock (_stateGate) Report(_monitorsOn && !_screenSaverRunning);
    }

    public void Dispose()
    {
        if (_desktopSwitchHook != IntPtr.Zero)
        {
            UnhookWinEvent(_desktopSwitchHook);
            _desktopSwitchHook = IntPtr.Zero;
        }

        _registration?.Dispose();
        _registration = null;
    }

    /// <summary>Owns an <c>HPOWERNOTIFY</c> and unregisters it on release.</summary>
    private sealed class PowerSettingNotificationHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => PowerSettingUnregisterNotification(handle) == ERROR_SUCCESS;
    }
}
#endif
