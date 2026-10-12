using LoupixDeck.Models;
using SkiaSharp;

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

    /// <summary>
    /// Whether the video frames currently carry the side strips, so a strip must be rendered
    /// without its background and handed over through <see cref="TryRedirectStripRedraw"/>.
    /// </summary>
    bool CarriesStrips { get; }

    /// <summary>
    /// Takes one side strip's redraw over while a clip is playing: <paramref name="foreground"/>
    /// (the strip without its background) is drawn over its column on every following video
    /// frame instead of being pushed on its own, which the next frame would overwrite. Always
    /// takes ownership of the bitmap. Returns false when no clip is playing, and the caller then
    /// does its normal push.
    /// </summary>
    bool TryRedirectStripRedraw(RotarySide side, SKBitmap foreground);

    /// <summary>
    /// Raised when <see cref="CarriesStrips"/> may have changed — playback started, stopped,
    /// paused or resumed — so the strips can be redrawn for the new owner of their pixels.
    /// </summary>
    event Action StripOwnerChanged;
}
