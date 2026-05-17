using LoupixDeck.Registry;

namespace LoupixDeck.Utils;

/// <summary>
/// Resolves which device's config should be loaded on startup.
///
/// Priority (after legacy config.json migration):
///   1. Hardware scan — if exactly one supported device is currently plugged in,
///      use it. The user's intent is "boot whatever's connected", not "boot
///      whatever was last touched".
///   2. Marker file (.active-device) — written after every successful start;
///      used both when multiple supported devices are plugged in (tie-break)
///      and when none are plugged in (offline launch / device disconnected).
///   3. Single existing per-device config — if exactly one exists, use it.
///   4. null → caller runs InitSetup so the user picks.
///
/// FakeDeviceOverride is applied at the very end so the testing flow can
/// pretend the resolved device is something else.
/// </summary>
public static class ActiveDeviceResolver
{
    private const string MarkerFile = ".active-device";

public static DeviceRegistry.DeviceInfo Resolve()
    {
        var resolved = ResolveCore();
#if DEBUG
        return FakeDeviceOverride.Apply(resolved);
#else
        return resolved;
#endif
    }

    /// <summary>Persist the slug of the device we just booted into so the next
    /// launch can prefer the same one when the hardware-scan is ambiguous.</summary>
    public static void RememberActive(DeviceRegistry.DeviceInfo info)
    {
        if (info == null) return;
        try
        {
            var path = Path.Combine(FileDialogHelper.GetConfigDir(), MarkerFile);
            File.WriteAllText(path, info.Slug);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActiveDeviceResolver] Failed to write marker: {ex.Message}");
        }
    }

private static DeviceRegistry.DeviceInfo ResolveCore()
    {
        var dir = FileDialogHelper.GetConfigDir();

        // 1a. Legacy config.json — migrate to Live S's per-device path.
        var legacy = Path.Combine(dir, "config.json");
        if (File.Exists(legacy))
        {
            var liveS = DeviceRegistry.GetDeviceByVidPid("2ec2", "0006");
            if (liveS != null)
            {
                var target = FileDialogHelper.GetConfigPath(liveS);
                if (!File.Exists(target))
                {
                    try { File.Move(legacy, target); }
                    catch (Exception ex) { Console.WriteLine($"Legacy config migration failed: {ex.Message}"); }
                }
            }
        }

        // 1. Hardware scan: trust what's actually plugged in.
        var connected = ScanConnectedDevices();
        if (connected.Count == 1)
        {
            Console.WriteLine($"[ActiveDeviceResolver] Single connected device: {connected[0].Name}");
            return connected[0];
        }

        var marker = ReadMarker();

        // 2a. Multiple connected → prefer marker if it matches one of them.
        if (connected.Count > 1)
        {
            var preferred = connected.FirstOrDefault(d => d.Slug == marker);
            if (preferred != null)
            {
                Console.WriteLine($"[ActiveDeviceResolver] Multiple connected ({connected.Count}); marker picked {preferred.Name}");
                return preferred;
            }
            Console.WriteLine($"[ActiveDeviceResolver] Multiple connected ({connected.Count}) and no marker match — InitSetup");
            return null;
        }

        // 0 connected. Fall back to existing configs.
        var configs = DeviceRegistry.SupportedDevices
            .Where(d => File.Exists(FileDialogHelper.GetConfigPath(d)))
            .ToList();

        if (configs.Count == 0) return null;

        // 2b. Marker wins among configs if it points to one of them.
        var byMarker = configs.FirstOrDefault(d => d.Slug == marker);
        if (byMarker != null)
        {
            Console.WriteLine($"[ActiveDeviceResolver] No device connected; marker config: {byMarker.Name}");
            return byMarker;
        }

        // 3. Exactly one config → unambiguous.
        if (configs.Count == 1)
        {
            Console.WriteLine($"[ActiveDeviceResolver] No device connected; only config: {configs[0].Name}");
            return configs[0];
        }

        // 4. Multiple configs, no marker → ask the user.
        Console.WriteLine($"[ActiveDeviceResolver] Ambiguous ({configs.Count} configs, no marker) — InitSetup");
        return null;
    }

private static List<DeviceRegistry.DeviceInfo> ScanConnectedDevices()
    {
        try
        {
            return SerialDeviceHelper.ListSerialUsbDevices()
                .Select(d => DeviceRegistry.GetDeviceByVidPid(d.Vid, d.Pid))
                .Where(d => d != null)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActiveDeviceResolver] USB scan failed: {ex.Message}");
            return new List<DeviceRegistry.DeviceInfo>();
        }
    }

    private static string ReadMarker()
    {
        try
        {
            var path = Path.Combine(FileDialogHelper.GetConfigDir(), MarkerFile);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
