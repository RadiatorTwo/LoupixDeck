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
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsDisplayStateService : DisplayStateServiceBase, IDisposable
{
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private const uint DEVICE_NOTIFY_CALLBACK = 2;
    private const uint PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint ERROR_SUCCESS = 0;
    private const uint DisplayOff = 0;

    // Offsets in POWERBROADCAST_SETTING: GUID PowerSetting, DWORD DataLength, UCHAR Data[].
    private const int DataLengthOffset = 16;
    private const int DataOffset = 20;

    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    // Held as a field so the GC cannot collect the delegate the power manager calls back into.
    private DeviceNotifyCallbackRoutine _callback;
    private PowerSettingNotificationHandle _registration;

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(in Guid settingGuid, uint flags,
        in DeviceNotifySubscribeParameters recipient, out PowerSettingNotificationHandle registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(IntPtr registrationHandle);

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
            Report(state != DisplayOff);
        }
        catch
        {
            /* ignore - never crash the native callback */
        }

        return ERROR_SUCCESS;
    }

    public void Dispose()
    {
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
