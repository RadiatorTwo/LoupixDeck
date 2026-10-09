namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Reports when the host's monitors go dark and when they wake (issue #382), separate from
/// system suspend: the displays turn off after the OS idle time while the machine keeps
/// running. Where the platform allows, it also reports the desktop screen saver, which keeps the
/// monitors powered. Every implementation is driven by an OS notification, none polls. On
/// platforms without a usable source <see cref="IsSupported"/> stays false and no event fires.
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

    /// <summary>The desktop screen saver started. Raised on an arbitrary thread.</summary>
    event EventHandler ScreenSaverStarted;

    /// <summary>The desktop screen saver ended. Raised on an arbitrary thread.</summary>
    event EventHandler ScreenSaverStopped;

    /// <summary>True when this platform reports the desktop screen saver (Windows).</summary>
    bool IsScreenSaverSupported { get; }

    /// <summary>Whether the desktop screen saver is running, as last reported.</summary>
    bool ScreenSaverRunning { get; }

    /// <summary>Attaches to the platform source. Idempotent; called on the UI thread.</summary>
    void StartMonitoring();
}
