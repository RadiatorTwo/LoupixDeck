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

    /// <summary>Re-renders one key on the next frame, after its content changed.</summary>
    void InvalidateButton(int index);

    /// <summary>Re-renders every key on the next frame.</summary>
    void InvalidateAllButtons();
}
