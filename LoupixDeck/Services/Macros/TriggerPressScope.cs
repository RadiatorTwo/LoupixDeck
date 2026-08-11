namespace LoupixDeck.Services.Macros;

/// <summary>
/// Ambient handoff of the <see cref="TriggerPress"/> that started the current command
/// dispatch (#185). The device controller enters a scope around the dispatch call; the
/// ambient flows across awaits and into <c>Task.Run</c> via ExecutionContext, so
/// <see cref="MacroRunner"/> can pick the press up without a new parameter on the
/// plugin-visible command surface. Same shape as <see cref="Services.DeviceRouter"/>,
/// but stateless, hence static.
///
/// <see cref="Current"/> is null whenever a macro was started by anything other than a
/// physical press (editor test run, stop hotkey, plugin, IPC) — a "wait for release" then
/// falls through immediately instead of hanging.
/// </summary>
public static class TriggerPressScope
{
    private static readonly AsyncLocal<TriggerPress> Ambient = new();

    /// <summary>The press that triggered the current flow, or null when there is none.</summary>
    public static TriggerPress Current => Ambient.Value;

    /// <summary>Make <paramref name="press"/> ambient until the returned scope is disposed.
    /// Nestable; restores the previous ambient on dispose.</summary>
    public static IDisposable Enter(TriggerPress press)
    {
        TriggerPress previous = Ambient.Value;
        Ambient.Value = press;
        return new Scope(previous);
    }

    private sealed class Scope(TriggerPress previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
