using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>Lifecycle state of a macro run, surfaced for runtime feedback.</summary>
public enum MacroExecutionState
{
    Running,
    Waiting,
    Completed,
    Cancelled,
    Failed
}

public sealed class MacroExecutionEventArgs(string macroName, MacroExecutionState state) : EventArgs
{
    public string MacroName { get; } = macroName;
    public MacroExecutionState State { get; } = state;
}

/// <summary>
/// App-global gatekeeper that enforces each macro's <see cref="MacroExecutionMode"/> across
/// all devices. Macros are identified by name (a macro can be bound on several devices), so
/// admission and the active-run table live here rather than in the per-device runner. Also
/// the single point where run-state transitions are logged and broadcast.
/// </summary>
public interface IMacroExecutionRegistry
{
    /// <summary>
    /// Atomically decides whether a new run may start and records it. Returns false (skip the
    /// run) for <see cref="MacroExecutionMode.RunOnce"/> while the macro is already running;
    /// for <see cref="MacroExecutionMode.RestartOnTrigger"/> it cancels the in-flight run(s)
    /// first. On true, <paramref name="cts"/> is tracked until <see cref="End"/> is called.
    /// </summary>
    bool TryBegin(Macro macro, CancellationTokenSource cts);

    /// <summary>Removes a finished run from the active-run table.</summary>
    void End(string macroName, CancellationTokenSource cts);

    /// <summary>Logs a run-state transition and raises <see cref="ExecutionStateChanged"/>.</summary>
    void Report(string macroName, MacroExecutionState state);

    /// <summary>Raised on every <see cref="Report"/> so the UI / plugins can observe progress.</summary>
    event EventHandler<MacroExecutionEventArgs> ExecutionStateChanged;
}

/// <inheritdoc cref="IMacroExecutionRegistry"/>
public sealed class MacroExecutionRegistry : IMacroExecutionRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<CancellationTokenSource>> _active =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryBegin(Macro macro, CancellationTokenSource cts)
    {
        if (macro == null || cts == null)
            return false;

        var name = macro.Name?.Trim() ?? string.Empty;

        lock (_lock)
        {
            _active.TryGetValue(name, out var list);
            var running = list is { Count: > 0 };

            switch (macro.ExecutionMode)
            {
                case MacroExecutionMode.RunOnce when running:
                    return false;

                case MacroExecutionMode.RestartOnTrigger when running:
                    // Cancel the in-flight run(s); each removes itself via End() as it unwinds.
                    foreach (var existing in list)
                    {
                        try { existing.Cancel(); }
                        catch (ObjectDisposedException) { /* already finished */ }
                    }
                    break;
            }

            if (list == null)
                _active[name] = list = [];
            list.Add(cts);
            return true;
        }
    }

    public void End(string macroName, CancellationTokenSource cts)
    {
        var name = macroName?.Trim() ?? string.Empty;

        lock (_lock)
        {
            if (!_active.TryGetValue(name, out var list))
                return;

            list.Remove(cts);
            if (list.Count == 0)
                _active.Remove(name);
        }
    }

    public event EventHandler<MacroExecutionEventArgs> ExecutionStateChanged;

    public void Report(string macroName, MacroExecutionState state)
    {
        var name = macroName?.Trim() ?? string.Empty;

        // Errors/cancellations go to stderr; normal progress to stdout.
        var line = $"[Macro] '{name}' {state}.";
        if (state is MacroExecutionState.Failed or MacroExecutionState.Cancelled)
            Console.Error.WriteLine(line);
        else
            Console.WriteLine(line);

        ExecutionStateChanged?.Invoke(this, new MacroExecutionEventArgs(name, state));
    }
}
