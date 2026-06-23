namespace LoupixDeck.Services;

/// <summary>
/// Routes a (single, root-resident) plugin's host calls to the device that
/// triggered them (issue #116 phase 2). Plugins are loaded once — their host
/// delegates resolve device-bound services through <see cref="Current"/>.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is the per-async-flow ambient set via <see cref="Enter"/>
/// (during a device's command dispatch, input handling and dynamic-text render),
/// falling back to <see cref="Default"/> (the primary device) for spontaneous
/// plugin callbacks that run outside any device flow (their own timers/events).
/// The ambient flows across awaits and into <c>Task.Run</c> via ExecutionContext.
/// </remarks>
public interface IDeviceRouter
{
    /// <summary>Fallback device provider (the primary) for calls with no ambient.</summary>
    IServiceProvider Default { get; set; }

    /// <summary>The ambient device provider for the current flow, else <see cref="Default"/>.</summary>
    IServiceProvider Current { get; }

    /// <summary>Make <paramref name="device"/> the ambient device until the returned
    /// scope is disposed. Nestable; restores the previous ambient on dispose.</summary>
    IDisposable Enter(IServiceProvider device);
}

public sealed class DeviceRouter : IDeviceRouter
{
    private static readonly AsyncLocal<IServiceProvider> Ambient = new();

    public IServiceProvider Default { get; set; }

    public IServiceProvider Current => Ambient.Value ?? Default;

    public IDisposable Enter(IServiceProvider device)
    {
        var previous = Ambient.Value;
        Ambient.Value = device;
        return new Scope(previous);
    }

    private sealed class Scope(IServiceProvider previous) : IDisposable
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
