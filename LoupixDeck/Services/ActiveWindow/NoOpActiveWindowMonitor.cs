namespace LoupixDeck.Services.ActiveWindow;

/// <summary>Fallback for platforms without a foreground-window hook
/// (pure Wayland, macOS and everything else).</summary>
public sealed class NoOpActiveWindowMonitor : IActiveWindowMonitor
{
    public event EventHandler<ActiveWindowInfo> ActiveWindowChanged { add { } remove { } }
    public ActiveWindowMonitorKind Kind => ActiveWindowMonitorKind.None;
    public void StartMonitoring() { }
}