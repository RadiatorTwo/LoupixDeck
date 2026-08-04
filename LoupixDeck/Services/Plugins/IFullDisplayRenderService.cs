using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Per-device single-owner coordinator for plugin full-display raw-BGRA rendering (issue #124).
/// Parallel to <see cref="IExclusiveModeService"/>, but instead of per-slot PNG tiles it drives a
/// plugin <see cref="IFullDisplayRenderer"/> on the central animation scheduler, pushing one
/// contiguous framebuffer per frame. While a renderer owns the display the controller suppresses its
/// own rendering, exactly as for the screensaver.
/// </summary>
public interface IFullDisplayRenderService
{
    /// <summary>True while a renderer currently owns the device's display.</summary>
    bool IsActive { get; }

    /// <summary>Tries to take the display over with <paramref name="renderer"/>. Returns a session
    /// handle, or null when the display is already owned by another full-display renderer, exclusive
    /// mode is active, there is no active device, or the plugin's
    /// <see cref="IFullDisplayRenderer.OnStart"/> failed.</summary>
    IFullDisplayRenderSession TryEnter(IFullDisplayRenderer renderer);

    /// <summary>Pauses/resumes frame pulls without releasing the active session (used when the device
    /// goes inactive). No-op when nothing is active.</summary>
    void SetPaused(bool paused);

    /// <summary>Releases the active session, if any, from the host side (e.g. on a profile/workspace
    /// switch). The owning plugin's session becomes inactive; it does not auto-restart — the plugin
    /// re-enters via its own command. No-op when nothing is active.</summary>
    void StopActive();

    /// <summary>Fired when a renderer takes the display over. The controller suppresses its own
    /// rendering and detaches side-strip providers, mirroring the screensaver.</summary>
    event Action Started;

    /// <summary>Fired when the renderer releases the display. The controller repaints the active
    /// page.</summary>
    event Action Stopped;
}
