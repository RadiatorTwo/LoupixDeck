namespace LoupixDeck.Services.Animation;

/// <summary>
/// Per-device coordinator for animated image layers on the Loupedeck CT wheel screen (issue #394).
/// Owns the single <see cref="WheelAnimationSource"/> registered on the central
/// <see cref="IAnimationScheduler"/>, rebuilds its entry set when the active wheel mode, the touch
/// page or the wheel canvas changes, and pauses it while the screensaver owns the display.
/// Mirrors <see cref="ISideDisplayAnimationManager"/> for the wheel.
/// </summary>
public interface IWheelAnimationManager
{
    /// <summary>Subscribes to page/mode/screensaver events and registers the source. Call once the device is up.</summary>
    void Start();

    /// <summary>Rebuilds the animated-layer entry set from the active wheel mode's canvas. Call after
    /// edits that may add/remove/retarget animated wheel content.</summary>
    void Rescan();
}
