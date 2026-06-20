namespace LoupixDeck.Services.ActiveWindow;

/// <summary>
/// Caches the most recent foreground window reported by <see cref="IActiveWindowMonitor"/>
/// so macro conditions can query it synchronously (the monitor itself is event-only).
/// Until the first window-change event arrives — and on platforms where the monitor is a
/// no-op (pure Wayland, macOS) — <see cref="Current"/> is an empty snapshot, which the
/// condition evaluator treats as "no match".
/// </summary>
public interface IActiveWindowState
{
    ActiveWindowInfo Current { get; }
}

/// <inheritdoc cref="IActiveWindowState"/>
public sealed class ActiveWindowState : IActiveWindowState
{
    private static readonly ActiveWindowInfo Empty = new() { ProcessName = string.Empty, Title = string.Empty };

    private volatile ActiveWindowInfo _current = Empty;

    public ActiveWindowInfo Current => _current;

    public ActiveWindowState(IActiveWindowMonitor monitor)
    {
        monitor.ActiveWindowChanged += (_, info) => _current = info ?? Empty;
        // Idempotent across the app — the per-device AppSwitchingService may also start it.
        monitor.StartMonitoring();
    }
}
