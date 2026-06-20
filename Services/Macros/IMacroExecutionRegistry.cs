using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// App-global gatekeeper that enforces each macro's <see cref="MacroExecutionMode"/> across
/// all devices. Macros are identified by name (a macro can be bound on several devices), so
/// admission and the active-run table live here rather than in the per-device runner.
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
}

/// <inheritdoc cref="IMacroExecutionRegistry"/>
public sealed class MacroExecutionRegistry : IMacroExecutionRegistry
{
    private readonly object _lock = new();
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
}
