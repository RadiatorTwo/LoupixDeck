namespace LoupixDeck.Services.HotPlug;

/// <summary>
/// OS-native USB topology change notifier (issue #116 phase 3b). Raises
/// <see cref="DevicesChanged"/> whenever a serial/USB device is added or removed;
/// it deliberately does NOT diff — it only signals "the device set may have
/// changed". <see cref="IHotPlugManager"/> debounces these signals and rescans to
/// figure out exactly which device appeared or disappeared.
/// </summary>
/// <remarks>
/// The event can fire on an arbitrary background thread.
/// </remarks>
public interface IDeviceWatcher : IDisposable
{
    event Action DevicesChanged;

    /// <summary>Begin watching. Failures are swallowed (logged) — a watcher that
    /// can't start simply means no hot-plug, never a crash.</summary>
    void Start();
}
