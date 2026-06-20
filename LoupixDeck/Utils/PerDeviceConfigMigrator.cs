using LoupixDeck.Registry;

namespace LoupixDeck.Utils;

/// <summary>
/// One-time, on-startup file rename that scopes a device-type config onto a
/// concrete physical unit: <c>config_&lt;slug&gt;.json</c> → <c>config_&lt;slug&gt;_&lt;serial&gt;.json</c>.
///
/// This is a FILENAME migration, distinct from <c>IConfigMigration</c> (which
/// migrates JSON content/version). It only runs for the currently connected
/// device, because the serial is only known for plugged-in hardware. Idempotent
/// and lossless: it never overwrites an existing scoped file and never touches a
/// slug-only file that belongs to a different unit.
/// </summary>
public static class PerDeviceConfigMigrator
{
    public static void Migrate(DeviceRegistry.DeviceInfo info, string serial)
    {
        if (info == null) return;

        // No usable serial → slug-only IS the canonical path; nothing to do.
        var scoped = FileDialogHelper.GetConfigPath(info, serial);
        var slugOnly = FileDialogHelper.GetConfigPath(info);
        if (string.Equals(scoped, slugOnly, StringComparison.Ordinal)) return;

        try
        {
            // Already scoped (this or an earlier launch) → idempotent no-op.
            if (File.Exists(scoped)) return;

            if (!File.Exists(slugOnly)) return;

            File.Move(slugOnly, scoped);
            Console.WriteLine($"[PerDeviceConfigMigrator] Scoped config '{Path.GetFileName(slugOnly)}' → '{Path.GetFileName(scoped)}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PerDeviceConfigMigrator] Scoping failed: {ex.Message}");
        }
    }
}
