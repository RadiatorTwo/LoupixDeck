using System.Runtime.Versioning;
using LoupixDeck.WaylandFocus;

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
/// app-switching rules keep matching unchanged. <see cref="FocusEventGate"/>
/// resolves each name once per app_id and drops duplicate events before
/// they reach the pipeline.
///
/// Registration only selects this monitor after compositor detection already
/// succeeded (GNOME/KDE never start it); the paths below cover what detection
/// cannot see — a stale socket, or a compositor without the focus protocol.
/// <see cref="FocusStreamRunner"/> keeps the stream alive across a
/// compositor restart — a logged reconnect with a capped backoff; setup
/// failures and streams that never come back log one line and hand the
/// session to an internal <see cref="LinuxActiveWindowMonitor"/>, so
/// XWayland windows keep working instead of the monitor dying silently.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class WaylandActiveWindowMonitor : IActiveWindowMonitor, IDisposable
{
    public event EventHandler<ActiveWindowInfo> ActiveWindowChanged;

    public ActiveWindowMonitorKind Kind => ActiveWindowMonitorKind.Wayland;

    private CancellationTokenSource _cts;
    private bool _started;

    // Started when the Wayland source cannot come up; forwards its events as
    // if this monitor had produced them.
    private LinuxActiveWindowMonitor _fallback;
    private bool _disposed;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;

        // The session declared Wayland but exposes no Wayland display — treat it
        // as the X11-ish case it really is rather than watching nothing.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            FallBackToXprop("No WAYLAND_DISPLAY");
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorAsync(_cts.Token));
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            // Resolves process names once per app_id and drops repeats before they
            // reach the app-switching pipeline: terminals and browsers rename their
            // windows constantly, and on COSMIC every resolution is a full /proc scan.
            // Created outside the runner so its cache survives a reconnect.
            FocusEventGate gate = new();

            // The runner owns the stream lifecycle: hyprland and niri end silently when
            // their socket closes, so it logs the end and reconnects with a backoff;
            // setup failures and exhausted retries surface here as one exception.
            FocusStreamRunner runner = new(
                static () => CompositorDetector.Create(),
                static message => Console.WriteLine($"[AppSwitch] {message}"));

            await runner.RunAsync(focus =>
            {
                // null = keyboard focus sits on no toplevel (layer-shell surface such
                // as a launcher). Keeping the previous profile is the right deck
                // behaviour: the user is mid-launcher, not "in" a different application.
                if (focus is null)
                    return;

                FocusInfo next = gate.Take(focus);

                if (next is null)
                    return;

                ActiveWindowChanged?.Invoke(this, new ActiveWindowInfo
                {
                    ProcessName = next.ProcessName ?? string.Empty,
                    Title = next.Title ?? string.Empty,
                });
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown via Dispose — not an error.
        }
        catch (Exception ex)
        {
            // Degradation contract: one line, then hand over to xprop. Covers setup
            // failures (a stale socket after a compositor crash or desktop switch, a
            // compositor without the focus protocol) and a working stream that the
            // runner could not bring back within its backoff budget.
            FallBackToXprop($"Wayland focus unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Hands monitoring to <see cref="LinuxActiveWindowMonitor"/> when the Wayland
    /// source cannot run: detection can be wrong (a stale socket) or the compositor
    /// can lack the protocol, and XWayland windows must keep switching pages.
    /// </summary>
    private void FallBackToXprop(string reason)
    {
        if (_disposed) return;

        Console.WriteLine($"[AppSwitch] {reason} — falling back to xprop for XWayland windows.");
        LinuxActiveWindowMonitor fallback = new();
        fallback.ActiveWindowChanged += ForwardFallback;
        _fallback = fallback;
        fallback.StartMonitoring();
    }

    private void ForwardFallback(object sender, ActiveWindowInfo info)
        => ActiveWindowChanged?.Invoke(this, info);

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        if (_fallback != null)
        {
            _fallback.ActiveWindowChanged -= ForwardFallback;
            _fallback.Dispose();
        }
    }
}