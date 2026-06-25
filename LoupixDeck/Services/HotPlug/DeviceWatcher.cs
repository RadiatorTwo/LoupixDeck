using System.Diagnostics;
#if WINDOWS
using System.Management;
#endif

namespace LoupixDeck.Services.HotPlug;

/// <summary>
/// Factory + concrete <see cref="IDeviceWatcher"/> implementations. Picks the
/// native mechanism per OS (issue #116 phase 3b):
///   • Windows: WMI <c>Win32_DeviceChangeEvent</c> (extrinsic push event, backed by
///     WM_DEVICECHANGE — no polling, ~0 % idle cost).
///   • Linux:   a long-running <c>udevadm monitor</c> subprocess on the tty subsystem.
///   • Other:   a no-op watcher (hot-plug simply disabled).
/// Each only signals "something changed"; the manager rescans + diffs.
/// </summary>
public static class DeviceWatcher
{
    public static IDeviceWatcher Create()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
            return new WmiDeviceWatcher();
#endif
        if (OperatingSystem.IsLinux())
            return new UdevDeviceWatcher();

        return new NoOpDeviceWatcher();
    }
}

internal sealed class NoOpDeviceWatcher : IDeviceWatcher
{
    public event Action DevicesChanged { add { } remove { } }
    public void Start() { }
    public void Dispose() { }
}

#if WINDOWS
/// <summary>
/// Windows hot-plug via WMI. We subscribe to the <b>extrinsic</b>
/// <c>Win32_DeviceChangeEvent</c> with an <c>EventType = 2 OR EventType = 3</c>
/// (device arrival / removal) filter. This is a true push event backed by
/// WM_DEVICECHANGE — WMI is only woken when the device topology actually changes,
/// so it costs ~0 % CPU while idle.
/// </summary>
/// <remarks>
/// We deliberately do <i>not</i> use the intrinsic <c>__InstanceCreationEvent</c> /
/// <c>__InstanceDeletionEvent ... WITHIN n</c> pattern over <c>Win32_PnPEntity</c>:
/// intrinsic events are implemented by WMI re-enumerating and diffing the whole
/// (very large) target class every <c>WITHIN</c> seconds, which pins WmiPrvSE.exe at
/// a constant 1–2 % CPU. <c>Win32_DeviceChangeEvent</c> carries no per-device detail,
/// but that's fine — the event only signals "rescan"; <see cref="HotPlugManager"/>
/// does the diff (and self-re-arms to confirm a removal), so a USB-CDC serial port
/// that arrives/leaves as part of a composite device is still picked up by the rescan.
/// </remarks>
// SuppressMessage mirrors SerialDeviceHelper: the WMI types are
// [SupportedOSPlatform("windows")] but this whole class is gated behind the
// WINDOWS constant, so CA1416 is informational here.
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal sealed class WmiDeviceWatcher : IDeviceWatcher
{
    private ManagementEventWatcher _watcher;

    public event Action DevicesChanged;

    public void Start()
    {
        try
        {
            // EventType 2 = device arrival, 3 = device removal (push, no polling).
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += (_, _) =>
            {
                Console.WriteLine("[HotPlug] WMI device-change event");
                DevicesChanged?.Invoke();
            };
            _watcher.Start();
            Console.WriteLine("[HotPlug] WMI device watcher started (push, Win32_DeviceChangeEvent).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HotPlug] WMI device watcher failed to start: {ex.Message}");
            _watcher = null;
        }
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { /* best effort */ }
        try { _watcher?.Dispose(); } catch { /* best effort */ }
        _watcher = null;
    }
}
#endif

/// <summary>
/// Linux hot-plug by tailing <c>udevadm monitor --udev --subsystem-match=tty</c>.
/// Reusing udevadm (already used for device enumeration) avoids a hand-rolled
/// netlink socket; every emitted line means a tty add/remove just happened, so we
/// simply signal a rescan. The subprocess is killed on dispose.
/// </summary>
internal sealed class UdevDeviceWatcher : IDeviceWatcher
{
    private Process _proc;

    public event Action DevicesChanged;

    public void Start()
    {
        try
        {
            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "udevadm",
                    Arguments = "monitor --udev --subsystem-match=tty",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _proc.OutputDataReceived += (_, e) =>
            {
                // udevadm prints a header line plus one "UDEV [time] add|remove …"
                // line per event; any non-empty data line is a topology change.
                if (!string.IsNullOrWhiteSpace(e.Data) &&
                    (e.Data.Contains("add", StringComparison.OrdinalIgnoreCase) ||
                     e.Data.Contains("remove", StringComparison.OrdinalIgnoreCase)))
                {
                    DevicesChanged?.Invoke();
                }
            };

            _proc.Start();
            _proc.BeginOutputReadLine();
            Console.WriteLine("[HotPlug] udev device watcher started.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HotPlug] udev watcher failed to start: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (_proc is { HasExited: false })
                _proc.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        try { _proc?.Dispose(); } catch { /* best effort */ }
        _proc = null;
    }
}
