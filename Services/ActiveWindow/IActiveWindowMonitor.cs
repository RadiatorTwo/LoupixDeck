namespace LoupixDeck.Services.ActiveWindow;

/// <summary>
/// Foreground window snapshot used to match app-switching rules.
/// <see cref="ProcessName"/> follows the same semantics on both OSes — the bare
/// executable/comm name without path or ".exe" suffix (e.g. "chrome").
/// </summary>
public sealed class ActiveWindowInfo
{
    public string ProcessName { get; init; }
    public string Title { get; init; }
}

/// <summary>
/// Watches the OS foreground window and raises <see cref="ActiveWindowChanged"/>
/// whenever it changes. Per-OS implementations mirror <c>ISystemPowerService</c>:
/// a real hook on Windows, an <c>xprop -spy</c> subprocess on X11/XWayland, and a
/// no-op everywhere else (pure Wayland, macOS, …).
/// </summary>
public interface IActiveWindowMonitor
{
    event EventHandler<ActiveWindowInfo> ActiveWindowChanged;
    void StartMonitoring();
}
