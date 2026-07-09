namespace LoupixDeck.Services.Animation;

/// <summary>
/// Per-device coordinator for animated side-display content (issue #123). Owns the single
/// <see cref="SideDisplayAnimationSource"/> registered on the central <see cref="IAnimationScheduler"/>,
/// rebuilds its entry set when the active left/right rotary page or its strip canvas changes, and
/// pauses/resumes it when another feature (screensaver, exclusive-mode plugin takeover, folder
/// navigation) owns the display. Mirrors <see cref="IButtonAnimationManager"/> for the side strips.
/// </summary>
public interface ISideDisplayAnimationManager
{
    /// <summary>Subscribes to page/takeover events and registers the source. Call once the device is up.</summary>
    void Start();

    /// <summary>Rebuilds the animated-strip entry set from the current left/right rotary pages. Call
    /// after edits that may add/remove/retarget animated strip content (mirrors
    /// <see cref="IButtonAnimationManager.Rescan"/>).</summary>
    void Rescan();
}
