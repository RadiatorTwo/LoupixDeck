using System.Runtime.Versioning;
using LoupixDeck.WaylandFocus;
using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.Services.ActiveWindow;

/// <summary>
/// Tracks the foreground window on pure Wayland sessions by consuming the
/// compositor's own focus IPC (COSMIC, sway, niri, Hyprland) through the
/// <c>LoupixDeck.WaylandFocus</c> library. Where the compositor exposes a
/// PID (sway, niri, Hyprland) the owning process is read exactly from
/// /proc; COSMIC's protocol withholds PIDs, so the owning process is
/// recovered from the app_id via a layered /proc + .desktop-file match.
/// Either way <see cref="ActiveWindowInfo.ProcessName"/> keeps the same
/// /proc comm semantics the xprop path produced, so existing
/// app-switching rules keep matching unchanged.
///
/// Mirrors <see cref="LinuxActiveWindowMonitor"/>'s degradation contract:
/// any failure — unsupported compositor, wedged socket, protocol error —
/// logs one line and leaves the monitor as a silent no-op rather than
/// taking the app down.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class WaylandActiveWindowMonitor : IActiveWindowMonitor, IDisposable
{
    public event EventHandler<ActiveWindowInfo> ActiveWindowChanged;

    private CancellationTokenSource _cts;
    private bool _started;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;

        // Same gating idea as the xprop monitor, mirrored: without a Wayland
        // session there is nothing to watch, so stay a no-op.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Console.WriteLine("[AppSwitch] No WAYLAND_DISPLAY — Wayland active-window monitor disabled.");
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorAsync(_cts.Token));
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            IFocusSource source = CompositorDetector.Create();
            Console.WriteLine($"[AppSwitch] Wayland monitor active (compositor: {source.Compositor}).");

            // The switch payload the matcher sees; compared before raising so
            // a title-churning app (terminals rename themselves constantly)
            // cannot flood the app-switching pipeline with duplicates.
            string lastProcess = null;
            string lastTitle = null;

            await foreach (FocusInfo focus in source.WatchAsync(ct))
            {
                // null = keyboard focus sits on no toplevel (layer-shell
                // surface such as a launcher). Keeping the previous profile
                // is the right deck behaviour: the user is mid-launcher, not
                // "in" a different application.
                if (focus is null)
                    continue;

                FocusInfo info = ProcessNamer.Enrich(focus);
                string process = info.ProcessName ?? string.Empty;
                string title = info.Title ?? string.Empty;

                if (process == lastProcess && title == lastTitle)
                    continue;
                lastProcess = process;
                lastTitle = title;


                ActiveWindowChanged?.Invoke(this, new ActiveWindowInfo
                {
                    ProcessName = process,
                    Title = title,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown via Dispose — not an error.
        }
        catch (Exception ex)
        {
            // Degradation contract: one line, then silence. Covers an
            // unsupported compositor (GNOME/KDE expose no focus IPC), a
            // stale socket file after a compositor crash, and any
            // protocol-level surprise the library could not absorb.
            Console.WriteLine($"[AppSwitch] Wayland active-window monitor unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}