using System.ComponentModel;
using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Services.ActiveWindow;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.AppSwitching;

namespace LoupixDeck.Services.ProcessWatch;

public interface IRunningIndicatorManager
{
    void Start();

    /// <summary>Re-reads which keys of the current page show a running indicator.</summary>
    void Rescan();
}

/// <summary>
/// Keeps the running dot of the current page's application keys up to date (issue #568):
/// green while the launched program runs, orange while one of its windows has focus.
/// </summary>
/// <remarks>
/// <para>
/// Event-driven only — there is deliberately no timer. The process table is read once whenever
/// something says it may have changed: the page is shown or a key's command is edited, a window
/// of a watched program takes focus, or LoupixDeck launches one (looked up again two seconds
/// later, once, because a program needs a moment to appear). An exit arrives from the kernel
/// through <see cref="IProcessWatcher.WatchExit"/>.
/// </para>
/// <para>
/// The price of not polling: a program started outside LoupixDeck that never takes focus, such as
/// one autostarting into the tray, gets its dot at the next of those events rather than at once.
/// On macOS no focus changes are reported, so the dot never turns orange there.
/// </para>
/// <para>All state is touched on the UI thread only.</para>
/// </remarks>
public sealed class RunningIndicatorManager(
    IPageManager pageManager,
    IProcessWatcher processWatcher,
    IActiveWindowMonitor activeWindowMonitor,
    IActiveWindowState activeWindowState) : IRunningIndicatorManager, IDisposable
{
    private static readonly TimeSpan LaunchSettleDelay = TimeSpan.FromSeconds(2);

    /// <summary>One watched program and the keys showing its dot.</summary>
    private sealed class Watch(string processName)
    {
        public string ProcessName { get; } = processName;
        public List<TouchButton> Buttons { get; } = [];

        /// <summary>The program's processes from the last lookup, with their exit watches.</summary>
        public Dictionary<int, IDisposable> Processes { get; } = [];

        /// <summary>The names the program's processes run under, for matching the focused window.</summary>
        public HashSet<string> ProcessNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bumped by every lookup, so only the latest result is applied.</summary>
        public int Generation;
    }

    private readonly Dictionary<string, Watch> _watches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TouchButton> _subscribed = [];
    private bool _started;
    private bool _disposed;
    private bool _rescanQueued;

    public void Start()
    {
        if (_started) return;
        _started = true;

        pageManager.TouchLayoutChanged += Rescan;
        activeWindowMonitor.ActiveWindowChanged += OnActiveWindowChanged;
        processWatcher.Launched += OnLaunched;
        // Idempotent across the app; page switching by application may have started it already.
        activeWindowMonitor.StartMonitoring();

        Rescan();
    }

    public void Rescan()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Rescan);
            return;
        }

        _rescanQueued = false;
        if (_disposed) return;

        TouchButton[] buttons = pageManager.CurrentTouchButtonPage?.TouchButtons?
            .Where(button => button != null).ToArray() ?? [];

        foreach (TouchButton button in _subscribed)
        {
            button.PropertyChanged -= OnButtonPropertyChanged;
            // A key that left the screen drops its dot quietly; it is looked up again when shown.
            if (!buttons.Contains(button))
                button.SetRunningIndicator(RunningIndicatorState.None, repaint: false);
        }

        _subscribed.Clear();

        Dictionary<string, List<TouchButton>> wanted = new(StringComparer.OrdinalIgnoreCase);
        foreach (TouchButton button in buttons)
        {
            button.PropertyChanged += OnButtonPropertyChanged;
            _subscribed.Add(button);

            string processName = WatchedProcessName(button);
            if (string.IsNullOrEmpty(processName))
            {
                button.SetRunningIndicator(RunningIndicatorState.None, repaint: true);
                continue;
            }

            if (!wanted.TryGetValue(processName, out List<TouchButton> list))
                wanted[processName] = list = [];
            list.Add(button);
        }

        foreach (string name in _watches.Keys.Where(name => !wanted.ContainsKey(name)).ToList())
        {
            Unwatch(_watches[name]);
            _watches.Remove(name);
        }

        foreach ((string name, List<TouchButton> list) in wanted)
        {
            if (!_watches.TryGetValue(name, out Watch watch))
                _watches[name] = watch = new Watch(name);

            watch.Buttons.Clear();
            watch.Buttons.AddRange(list);
            Apply(watch);
            Lookup(watch);
        }
    }

    public void Dispose()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Dispose);
            return;
        }

        if (_disposed) return;
        _disposed = true;

        pageManager.TouchLayoutChanged -= Rescan;
        activeWindowMonitor.ActiveWindowChanged -= OnActiveWindowChanged;
        processWatcher.Launched -= OnLaunched;

        foreach (TouchButton button in _subscribed)
            button.PropertyChanged -= OnButtonPropertyChanged;
        _subscribed.Clear();

        foreach (Watch watch in _watches.Values)
            Unwatch(watch);
        _watches.Clear();
    }

    /// <summary>The process a key's dot follows, or null when the key shows none.</summary>
    private static string WatchedProcessName(TouchButton button)
    {
        if (button.IsFolderBackSlot) return null;

        LaunchAppSpec spec = LaunchAppSpec.FromCommand(button.Command);
        if (spec is not { ShowRunningIndicator: true }) return null;

        string name = spec.ResolveProcessName();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private void OnButtonPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TouchButton.Command) || _rescanQueued) return;

        // Several edits in one go (a paste, a template) rescan once.
        _rescanQueued = true;
        Dispatcher.UIThread.Post(Rescan);
    }

    private void OnActiveWindowChanged(object sender, ActiveWindowInfo info)
        => Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            string focused = ContextRuleMatcher.Normalize(info?.ProcessName);
            foreach (Watch watch in _watches.Values)
            {
                // A program that was not running a moment ago and now has a focused window has
                // just been started: look its processes up so their exit is watched.
                if (watch.Processes.Count == 0 && Matches(watch, focused))
                    Lookup(watch);
                else
                    Apply(watch, focused);
            }
        });

    private void OnLaunched(string processName)
        => Dispatcher.UIThread.Post(async () =>
        {
            if (_disposed || !_watches.ContainsKey(processName)) return;

            await Task.Delay(LaunchSettleDelay);
            if (!_disposed && _watches.TryGetValue(processName, out Watch watch))
                Lookup(watch);
        });

    /// <summary>Reads the process table once for <paramref name="watch"/>, off the UI thread.</summary>
    private async void Lookup(Watch watch)
    {
        int generation = ++watch.Generation;
        IReadOnlyList<RunningProcess> processes;
        try
        {
            processes = await Task.Run(() => processWatcher.FindRunning(watch.ProcessName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RunningIndicator] Lookup of '{watch.ProcessName}' failed: {ex.Message}");
            return;
        }

        // Back on the UI thread. A newer lookup, or a rescan that dropped the program, wins.
        if (_disposed || generation != watch.Generation ||
            !_watches.TryGetValue(watch.ProcessName, out Watch current) || current != watch)
        {
            return;
        }

        HashSet<int> pids = processes.Select(process => process.Pid).ToHashSet();
        foreach (int pid in watch.Processes.Keys.Where(pid => !pids.Contains(pid)).ToList())
        {
            watch.Processes[pid]?.Dispose();
            watch.Processes.Remove(pid);
        }

        foreach (int pid in pids.Where(pid => !watch.Processes.ContainsKey(pid)).ToList())
        {
            // Null when the process cannot be watched: it then counts as running until the next
            // lookup. An exit reported straight away is posted, so it finds the entry.
            watch.Processes[pid] = processWatcher.WatchExit(pid, () => OnExited(watch, pid));
        }

        watch.ProcessNames.Clear();
        foreach (RunningProcess process in processes)
            watch.ProcessNames.Add(ContextRuleMatcher.Normalize(process.Name));

        Apply(watch);
    }

    private void OnExited(Watch watch, int pid)
        => Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !watch.Processes.Remove(pid, out IDisposable subscription)) return;

            subscription?.Dispose();
            // One process of the program ended; whether the program still runs (another
            // instance, a restart that replaced it) takes one more look.
            Lookup(watch);
        });

    private void Apply(Watch watch)
        => Apply(watch, ContextRuleMatcher.Normalize(activeWindowState.Current?.ProcessName));

    private static void Apply(Watch watch, string focusedProcess)
    {
        RunningIndicatorState state = watch.Processes.Count == 0
            ? RunningIndicatorState.None
            : Matches(watch, focusedProcess)
                ? RunningIndicatorState.Active
                : RunningIndicatorState.Running;

        foreach (TouchButton button in watch.Buttons)
            button.SetRunningIndicator(state, repaint: true);
    }

    private static bool Matches(Watch watch, string focusedProcess)
    {
        if (string.IsNullOrEmpty(focusedProcess)) return false;

        return ContextRuleMatcher.ProcessNameMatches(ContextRuleMatcher.Normalize(watch.ProcessName), focusedProcess)
               || watch.ProcessNames.Any(name => ContextRuleMatcher.ProcessNameMatches(name, focusedProcess));
    }

    private static void Unwatch(Watch watch)
    {
        watch.Generation++;
        foreach (IDisposable subscription in watch.Processes.Values)
            subscription?.Dispose();
        watch.Processes.Clear();
    }
}
