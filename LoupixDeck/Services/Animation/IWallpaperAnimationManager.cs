namespace LoupixDeck.Services.Animation;

/// <summary>
/// Owns the video wallpaper of the active page for one device: starts playback when the page's
/// main wallpaper slot references a clip, stops it when it does not, and pauses it while another
/// feature owns the display.
/// </summary>
public interface IWallpaperAnimationManager
{
    /// <summary>Begins following the active page. Called once per device after the device is up.</summary>
    void Start();

    /// <summary>Whether a clip is currently being played behind the keys.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Takes one key's redraw over while a clip is playing: the key is re-rendered into the
    /// overlay of the next video frame instead of being pushed on its own, because a partial
    /// write racing a full-panel push tears. Returns false when no clip is playing, and the
    /// caller then does its normal partial push.
    /// </summary>
    bool TryRedirectButtonRedraw(int index);

    /// <summary>The whole-page equivalent, for a repaint that would push every key.</summary>
    bool TryRedirectPageRedraw();
}
