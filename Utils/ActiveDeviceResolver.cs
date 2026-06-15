using LoupixDeck.Registry;

namespace LoupixDeck.Utils;

/// <summary>
/// Resolves which device instance's config should be loaded on startup.
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
/// Everything is keyed by <see cref="ResolvedDevice.ScopeKey"/> (slug + serial),
/// so two physically identical units no longer collapse into one. The marker is
/// read back-compatibly (an old marker holds only a bare slug).
///
/// FakeDeviceOverride is applied at the very end so the testing flow can
/// pretend the resolved device is something else.
/// </summary>
public static class ActiveDeviceResolver
{
    private const string MarkerFile = ".active-device";

    public static ResolvedDevice Resolve()
    {
        var resolved = ResolveCore();
#if DEBUG
        return FakeDeviceOverride.Apply(resolved);
#else
        return resolved;
#endif
    }

    /// <summary>Persist the scope key (slug + serial) of the device we just booted
    /// into so the next launch can prefer the same one when the hardware-scan is
    /// ambiguous.</summary>
    public static void RememberActive(ResolvedDevice device)
    {
        if (device == null) return;
        try
        {
            var path = Path.Combine(FileDialogHelper.GetConfigDir(), MarkerFile);
            File.WriteAllText(path, device.ScopeKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActiveDeviceResolver] Failed to write marker: {ex.Message}");
        }
    }

    /// <summary>
    /// Enumerate ALL currently connected supported devices, each scoped onto its
    /// serial-keyed config (issue #116 phase 2). Used to bring up every device in
    /// parallel; falls back via <see cref="Resolve"/> when nothing is connected.
    /// </summary>
    public static List<ResolvedDevice> ResolveAll()
    {
        MigrateLegacyConfigJson();

        var result = new List<ResolvedDevice>();
        foreach (var d in ScanConnectedDevices())
        {
#if DEBUG
            var dev = FakeDeviceOverride.Apply(d);
#else
            var dev = d;
#endif
            result.Add(Scope(dev));
        }
        return result;
    }

    /// <summary>Pick which connected device owns the config window: the marker match,
    /// else the first. Returns null for an empty list.</summary>
    public static ResolvedDevice PickPrimary(IReadOnlyList<ResolvedDevice> devices)
    {
        if (devices == null || devices.Count == 0) return null;
        var marker = ReadMarker();
        return devices.FirstOrDefault(d => MarkerMatches(d, marker)) ?? devices[0];
    }

    /// <summary>Legacy config.json (pre-per-device) → Live S's slug-only path. Idempotent.</summary>
    private static void MigrateLegacyConfigJson()
    {
        var legacy = Path.Combine(FileDialogHelper.GetConfigDir(), "config.json");
        if (!File.Exists(legacy)) return;

        var liveS = DeviceRegistry.GetDeviceByVidPid("2ec2", "0006");
        if (liveS == null) return;

        var target = FileDialogHelper.GetConfigPath(liveS);
        if (File.Exists(target)) return;

        try { File.Move(legacy, target); }
        catch (Exception ex) { Console.WriteLine($"Legacy config migration failed: {ex.Message}"); }
    }

    private static ResolvedDevice ResolveCore()
    {
        // 1a. Legacy config.json — migrate to Live S's per-device (slug-only) path.
        MigrateLegacyConfigJson();

        var connected = ScanConnectedDevices();
        var marker = ReadMarker();

        // 1. Hardware scan: trust what's actually plugged in.
        if (connected.Count == 1)
        {
            Console.WriteLine($"[ActiveDeviceResolver] Single connected device: {connected[0].Info.Name} (serial: {connected[0].Serial ?? "<none>"})");
            return Scope(connected[0]);
        }

        // 2a. Multiple connected → prefer marker if it matches one of them.
        if (connected.Count > 1)
        {
            var preferred = connected.FirstOrDefault(d => MarkerMatches(d, marker));
            if (preferred != null)
            {
                Console.WriteLine($"[ActiveDeviceResolver] Multiple connected ({connected.Count}); marker picked {preferred.Info.Name}");
                return Scope(preferred);
            }
            Console.WriteLine($"[ActiveDeviceResolver] Multiple connected ({connected.Count}) and no marker match — InitSetup");
            return null;
        }

        // 0 connected. Fall back to existing configs.
        var configs = EnumerateConfigDevices();
        if (configs.Count == 0) return null;

        // 2b. Marker wins among configs if it points to one of them.
        var byMarker = configs.FirstOrDefault(d => MarkerMatches(d, marker));
        if (byMarker != null)
        {
            Console.WriteLine($"[ActiveDeviceResolver] No device connected; marker config: {byMarker.Info.Name}");
            return byMarker;
        }

        // 3. Exactly one config → unambiguous.
        if (configs.Count == 1)
        {
            Console.WriteLine($"[ActiveDeviceResolver] No device connected; only config: {configs[0].Info.Name}");
            return configs[0];
        }

        // 4. Multiple configs, no marker → ask the user.
        Console.WriteLine($"[ActiveDeviceResolver] Ambiguous ({configs.Count} configs, no marker) — InitSetup");
        return null;
    }

    /// <summary>
    /// Run the one-time slug-only → slug+serial file rename for a connected device
    /// (the serial is only known for plugged-in hardware) and return it unchanged.
    /// </summary>
    private static ResolvedDevice Scope(ResolvedDevice device)
    {
        PerDeviceConfigMigrator.Migrate(device.Info, device.Serial);
        return device;
    }

    private static bool MarkerMatches(ResolvedDevice d, string marker)
    {
        if (string.IsNullOrEmpty(marker)) return false;
        // Back-compat: an old marker holds the bare slug; a new one holds the scope key.
        return string.Equals(d.ScopeKey, marker, StringComparison.OrdinalIgnoreCase)
               || string.Equals(d.Slug, marker, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ResolvedDevice> ScanConnectedDevices()
    {
        try
        {
            return SerialDeviceHelper.ListSerialUsbDevices()
                .Select(d => new { d, info = DeviceRegistry.GetDeviceByVidPid(d.Vid, d.Pid) })
                .Where(x => x.info != null)
                .Select(x => new ResolvedDevice(x.info, x.d.NormalizedSerial))
                // Dedupe by (slug, serial), NOT by type — two identical units with
                // real serials stay distinct (the old .Distinct() collapsed them).
                .GroupBy(r => r.ScopeKey)
                .Select(g => g.First())
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActiveDeviceResolver] USB scan failed: {ex.Message}");
            return new List<ResolvedDevice>();
        }
    }

    /// <summary>
    /// Enumerate existing per-device config files (slug-only and slug+serial) and
    /// map each back to a <see cref="ResolvedDevice"/>. Used only when no hardware
    /// is connected, so the serial tail comes from the filename.
    /// </summary>
    private static List<ResolvedDevice> EnumerateConfigDevices()
    {
        var result = new List<ResolvedDevice>();
        try
        {
            var dir = FileDialogHelper.GetConfigDir();
            const string prefix = "config_";
            foreach (var path in Directory.EnumerateFiles(dir, "config_*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var remainder = name[prefix.Length..];

                foreach (var info in DeviceRegistry.SupportedDevices)
                {
                    if (string.Equals(remainder, info.Slug, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new ResolvedDevice(info, null));
                        break;
                    }
                    // slug + '_' + serial-tail (slugs never contain '_').
                    if (remainder.StartsWith(info.Slug + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        var tail = remainder[(info.Slug.Length + 1)..];
                        result.Add(new ResolvedDevice(info, tail));
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActiveDeviceResolver] Config enumeration failed: {ex.Message}");
        }

        return result;
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
