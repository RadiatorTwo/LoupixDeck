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
/// Which mechanism backs an <see cref="IActiveWindowMonitor"/>: decided once at
/// registration from the session type and the detected compositor, and the ground
/// truth diagnostics follow when reporting.
/// </summary>
public enum ActiveWindowMonitorKind
{
    /// <summary>Native compositor focus tracking (COSMIC, sway, niri, Hyprland).</summary>
    Wayland,

    /// <summary>
    /// An <c>xprop -spy</c> subprocess over X11/XWayland — also the fallback on Wayland
    /// sessions without a supported compositor.
    /// </summary>
    XProp,

    /// <summary>The Windows foreground-hook monitor.</summary>
    Windows,

    /// <summary>No mechanism; the monitor never reports.</summary>
    None,
}

/// <summary>
/// Watches the OS foreground window and raises <see cref="ActiveWindowChanged"/>
/// whenever it changes. Per-OS implementations mirror <c>ISystemPowerService</c>:
/// native compositor focus tracking or an <c>xprop -spy</c> subprocess on Linux
/// (the session and detected compositor decide which), a real hook on Windows,
/// and a no-op everywhere else (macOS, …).
/// </summary>
public interface IActiveWindowMonitor
{
    event EventHandler<ActiveWindowInfo> ActiveWindowChanged;

    /// <summary>The mechanism this monitor actually runs.</summary>
    ActiveWindowMonitorKind Kind { get; }

    void StartMonitoring();
}