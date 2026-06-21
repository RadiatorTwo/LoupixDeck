namespace LoupixDeck.Services.Animation;

/// <summary>
/// Per-device coordinator for per-button animations (issue #121). Owns the single
/// <see cref="ButtonAnimationSource"/> registered on the central <see cref="IAnimationScheduler"/>,
/// rebuilds its entry set when the active page or button bindings change, and pauses/resumes it when
/// another feature (screensaver, exclusive-mode plugin takeover, folder navigation) owns the display.
/// </summary>
public interface IButtonAnimationManager
{
    /// <summary>Subscribes to page/takeover events and registers the source. Call once the device is up.</summary>
    void Start();

    /// <summary>Rebuilds the animated-button entry set from the current page. Call after edits that
    /// may add/remove/retarget animated content (mirrors <c>IDynamicTextManager.Rescan</c>).</summary>
    void Rescan();
}
