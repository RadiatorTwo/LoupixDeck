namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Reports when the host's monitors go dark and when they wake (issue #382), separate from
/// system suspend: the displays turn off after the OS idle time while the machine keeps
/// running. Every implementation is driven by an OS notification, none polls. On platforms
/// without a usable source <see cref="IsSupported"/> stays false and no event fires.
/// </summary>
public interface IDisplayStateService
{
    /// <summary>All displays went off. Raised on an arbitrary thread.</summary>
    event EventHandler DisplaysOff;

    /// <summary>At least one display is on again. Raised on an arbitrary thread.</summary>
    event EventHandler DisplaysOn;

    /// <summary>True once a working display-state source is attached.</summary>
    bool IsSupported { get; }

    /// <summary>The last state the source reported; true until it says otherwise.</summary>
    bool DisplaysAreOn { get; }

    /// <summary>Attaches to the platform source. Idempotent; called on the UI thread.</summary>
    void StartMonitoring();
}
