using System.Diagnostics;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.WaylandFocus;

namespace LoupixDeck.Services.ProcessWatch;

/// <summary>
/// Exit notification for one platform. Implementations raise the callback once, on a thread of
/// their own, and never poll.
/// </summary>
internal interface IExitNotifier : IDisposable
{
    IDisposable Watch(int pid, Action onExit);
}

/// <inheritdoc cref="IProcessWatcher"/>
public sealed class ProcessWatcher : IProcessWatcher, IDisposable
{
    private readonly IExitNotifier _notifier = CreateNotifier();

    public event Action<string> Launched;

    public IReadOnlyList<RunningProcess> FindRunning(string processName)
    {
        string name = processName?.Trim();
        if (string.IsNullOrEmpty(name))
            return [];

        try
        {
            // ProcessNamer matches the way the Linux window activator does, so a Flatpak app_id or
            // a desktop-file name finds the same process the Single Instance check focuses.
            if (OperatingSystem.IsLinux())
                return ProcessNamer.FindByAppId(name)
                    .DistinctBy(match => match.Pid)
                    .Select(match => new RunningProcess(match.Pid, match.Name))
                    .ToList();

            Process[] processes = Process.GetProcessesByName(ContextRuleMatcher.Normalize(name));
            try
            {
                return processes.Select(process => new RunningProcess(process.Id, process.ProcessName)).ToList();
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ProcessWatch] Looking up '{name}' failed: {ex.Message}");
            return [];
        }
    }

    public IDisposable WatchExit(int pid, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        return _notifier?.Watch(pid, onExit);
    }

    public void NotifyLaunched(string processName)
    {
        if (!string.IsNullOrWhiteSpace(processName))
            Launched?.Invoke(processName.Trim());
    }

    public void Dispose() => _notifier?.Dispose();

    private static IExitNotifier CreateNotifier()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return new ProcessHandleExitNotifier();
            if (OperatingSystem.IsLinux())
                return new PidfdExitNotifier();
            if (OperatingSystem.IsMacOS())
                return new KqueueExitNotifier();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessWatch] Exit notification unavailable: {ex.Message}");
        }

        return null;
    }
}
