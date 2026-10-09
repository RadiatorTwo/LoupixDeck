namespace LoupixDeck.Services.SystemPower.Linux;

/// <summary>One way of learning whether the monitors are on, tried in turn by
/// <see cref="LinuxDisplayStateService"/>.</summary>
public interface ILinuxDisplaySource : IDisposable
{
    /// <summary>Name for the log.</summary>
    string Name { get; }

    /// <summary>
    /// Attaches to the source. Returns false when this session does not offer it. On success
    /// <paramref name="report"/> receives the current state and every change from then on
    /// (true = at least one display on), on an arbitrary thread.
    /// </summary>
    Task<bool> TryStartAsync(Action<bool> report);
}
