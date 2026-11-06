namespace LoupixDeck.Services.AppActivation;

/// <summary>What <see cref="IAppWindowActivator.ActivateAsync"/> found and did.</summary>
public enum AppActivationOutcome
{
    /// <summary>No process of that name runs. The caller starts the application.</summary>
    NotRunning,

    /// <summary>
    /// The process runs but owns no window that could be brought forward — typically an
    /// application minimized to the tray. The caller starts the application, which hands the
    /// second start to its running instance and so shows its window again.
    /// </summary>
    NoWindow,

    /// <summary>One of the application's windows already has focus; nothing was done.</summary>
    AlreadyFocused,

    /// <summary>A window of the application was brought to the foreground.</summary>
    Activated,

    /// <summary>
    /// The process runs, but this session offers no way to focus its window (GNOME on Wayland,
    /// an unknown compositor). Nothing was done: starting it again would open a second instance.
    /// </summary>
    Unsupported
}

/// <summary>
/// Finds the running instance of an application by process name and brings its window to the
/// foreground. Backs the "Single Instance" option of <c>System.LaunchApp</c>. Per-OS
/// implementations mirror <c>IActiveWindowMonitor</c>: a window search on Windows, the
/// compositor's IPC or X11 on Linux, and LaunchServices on macOS.
/// </summary>
public interface IAppWindowActivator
{
    /// <summary>
    /// Brings the application whose process is called <paramref name="processName"/> to the
    /// foreground, unless it already is. The name follows the context-rule semantics: the bare
    /// executable name without path or <c>.exe</c>; on Linux an app_id or window class is
    /// accepted as well.
    /// </summary>
    Task<AppActivationOutcome> ActivateAsync(string processName, CancellationToken cancellationToken);
}
