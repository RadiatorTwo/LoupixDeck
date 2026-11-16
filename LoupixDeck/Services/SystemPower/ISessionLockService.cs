namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Reports when the user's session is locked or otherwise not in front of the user — the lock
/// screen after a wake, a manual lock, or a switch to another account — and when it is back
/// (issue #522). While it is locked the device is switched off, so only controls set to run
/// while the device is off still work. On platforms without a usable source
/// <see cref="IsSupported"/> stays false and no event fires.
/// </summary>
public interface ISessionLockService
{
    /// <summary>The session was locked or switched away. Raised on an arbitrary thread.</summary>
    event EventHandler Locked;

    /// <summary>The session is unlocked and active again. Raised on an arbitrary thread.</summary>
    event EventHandler Unlocked;

    /// <summary>True once a working lock-state source is attached.</summary>
    bool IsSupported { get; }

    /// <summary>The last state the source reported; false until it says otherwise.</summary>
    bool IsLocked { get; }

    /// <summary>Attaches to the platform source. Idempotent; called on the UI thread.</summary>
    void StartMonitoring();
}
