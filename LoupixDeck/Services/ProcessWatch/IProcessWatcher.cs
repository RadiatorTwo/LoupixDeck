namespace LoupixDeck.Services.ProcessWatch;

/// <summary>A process found by <see cref="IProcessWatcher.FindRunning"/>, with its own name.</summary>
/// <remarks>
/// <see cref="Name"/> can differ from the name looked up: on Linux a Flatpak id or desktop-file
/// name resolves to a binary called something else, and that binary's name is what the
/// foreground-window monitor reports.
/// </remarks>
public sealed record RunningProcess(int Pid, string Name);

/// <summary>
/// Finds running applications by process name and reports when they exit, for the running
/// indicator of <c>System.LaunchApp</c> keys (issue #568).
/// </summary>
/// <remarks>
/// Nothing here polls. A lookup is a single snapshot taken when the caller asks for one, and an
/// exit is reported by the kernel: a wait on the process handle on Windows, a pidfd on Linux and
/// a kqueue on macOS.
/// </remarks>
public interface IProcessWatcher
{
    /// <summary>
    /// The processes called <paramref name="processName"/>, in context-rule semantics
    /// (bare executable name without path or <c>.exe</c>; on Linux an app_id is accepted too).
    /// Empty when none runs. Reads the process table, so callers keep it off the UI thread.
    /// </summary>
    IReadOnlyList<RunningProcess> FindRunning(string processName);

    /// <summary>
    /// Calls <paramref name="onExit"/> once, on a background thread, when process
    /// <paramref name="pid"/> ends — immediately when it already has. Dispose the result to stop
    /// watching. Null when the process cannot be watched on this system (access denied, a kernel
    /// without pidfd).
    /// </summary>
    IDisposable WatchExit(int pid, Action onExit);

    /// <summary>Raised by <see cref="NotifyLaunched"/>.</summary>
    event Action<string> Launched;

    /// <summary>
    /// Tells listeners that LoupixDeck just started the application called
    /// <paramref name="processName"/>, so they can look for it once.
    /// </summary>
    void NotifyLaunched(string processName);
}
