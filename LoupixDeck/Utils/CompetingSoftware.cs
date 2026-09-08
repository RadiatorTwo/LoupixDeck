using System.Diagnostics;

namespace LoupixDeck.Utils;

/// <summary>
/// Closes the vendor software that would otherwise hold the device's serial port (COM) open — the
/// official Loupedeck app and, since Logitech's acquisition, the Logi plugin service. Only one
/// program can own the port at a time, so LoupixDeck shuts these down on startup before it connects.
/// Windows-only; a no-op elsewhere. Best-effort: a running Logi service may relaunch, but killing it
/// frees the port long enough for us to grab it first.
/// </summary>
public static class CompetingSoftware
{
    // Process image names that grab the Loupedeck/Razer serial port.
    private static readonly string[] ImageNames =
    [
        "Loupedeck.exe",
        "Loupedeck2.exe",
        "LoupedeckService.exe",
        "LogiPluginServiceExt.exe",
        "LogiPluginService.exe",
    ];

    /// <summary>Kill the known competing processes so the serial port is free. Safe to call every
    /// startup; never throws.</summary>
    public static void CloseAll()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Direct kill by image name (works even when process enumeration is restricted). Two passes
        // because a service may spawn a child that respawns the parent briefly.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            foreach (var image in ImageNames)
                TaskKill(image);
            System.Threading.Thread.Sleep(300);
        }

        // Fallback for future/renamed variants (LoupedeckPluginHost, LogiPluginService*, …).
        foreach (var name in new[] { "Loupedeck", "LogiPluginService" })
        {
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var proc in found)
            {
                try { proc.Kill(entireProcessTree: true); }
                catch { /* already gone / access denied — best-effort */ }
                finally { proc.Dispose(); }
            }
        }
    }

    private static void TaskKill(string image)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/F /T /IM {image}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(3000);
        }
        catch { /* taskkill missing or nothing to kill — ignore */ }
    }
}
