using System.Diagnostics;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Listens to the systemd-logind PrepareForSleep signal via dbus-monitor.
/// dbus-monitor is shipped with every systemd distro and avoids a Tmds.DBus
/// system-bus connection just for one signal.
/// </summary>
public sealed class LinuxSystemPowerService : ISystemPowerService, IDisposable
{
    public event EventHandler Suspending;
    public event EventHandler Resuming;

    private Process _proc;
    private bool _started;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(Monitor);
    }

    private void Monitor()
    {
        try
        {
            _proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dbus-monitor",
                Arguments = "--system \"type='signal',interface='org.freedesktop.login1.Manager',member='PrepareForSleep'\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (_proc == null) return;

            while (!_proc.StandardOutput.EndOfStream)
            {
                var line = _proc.StandardOutput.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;
                // PrepareForSleep(true) → going to sleep, (false) → waking up.
                if (line.Contains("boolean true"))
                    Suspending?.Invoke(this, EventArgs.Empty);
                else if (line.Contains("boolean false"))
                    Resuming?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Power] Linux monitor unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try { _proc?.Kill(); } catch { /* ignore */ }
        _proc?.Dispose();
    }
}
