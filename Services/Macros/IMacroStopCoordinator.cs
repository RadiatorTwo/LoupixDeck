namespace LoupixDeck.Services.Macros;

/// <summary>
/// App-global registry of every device's <see cref="MacroRunner"/>. The macro runners are
/// per-device, but the global stop hotkey is a single process-wide listener — it cancels
/// macros on every device through this coordinator.
/// </summary>
public interface IMacroStopCoordinator
{
    void Register(MacroRunner runner);
    void Unregister(MacroRunner runner);

    /// <summary>Cancels every running macro across all registered runners.</summary>
    void CancelAll();
}

public sealed class MacroStopCoordinator : IMacroStopCoordinator
{
    private readonly object _lock = new();
    private readonly List<MacroRunner> _runners = [];

    public void Register(MacroRunner runner)
    {
        lock (_lock)
        {
            if (!_runners.Contains(runner))
                _runners.Add(runner);
        }
    }

    public void Unregister(MacroRunner runner)
    {
        lock (_lock)
            _runners.Remove(runner);
    }

    public void CancelAll()
    {
        List<MacroRunner> snapshot;
        lock (_lock)
            snapshot = _runners.ToList();

        foreach (var runner in snapshot)
            runner.CancelAll();
    }
}
