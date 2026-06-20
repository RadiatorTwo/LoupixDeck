namespace LoupixDeck.Services.Screensaver;

/// <summary>
/// Per-device owner of the idle-driven full-display screensaver (issue #120). Watches an
/// idle countdown that any hardware input resets; when it elapses it takes the display
/// over (via exclusive mode) and plays the configured clip through the central animation
/// scheduler. The first input afterwards stops it and repaints the active page.
/// </summary>
public interface IScreensaverManager
{
    /// <summary>True while the screensaver is currently playing.</summary>
    bool IsRunning { get; }

    /// <summary>True when ffmpeg is reachable on PATH — the feature needs it to decode
    /// GIF/MP4. Surfaced in settings so the UI can hint when it's missing.</summary>
    bool IsFfmpegAvailable { get; }

    /// <summary>Starts idle monitoring. Call once the device is up and the page is drawn.</summary>
    void Arm();

    /// <summary>Resets the idle countdown and stops a running screensaver. Call at the top
    /// of every hardware-input handler. Returns true when this call stopped a screensaver
    /// that was running — i.e. the input was a "wake" gesture and the caller should consume
    /// it (not also run the button/touch/rotary action).</summary>
    bool NotifyActivity();

    /// <summary>Stops any running screensaver and the idle countdown. Call on shutdown.</summary>
    void Stop();

    /// <summary>Raised when the screensaver starts playing. The controller suppresses its
    /// own rendering (and stops side-strip provider timers) while it owns the display.</summary>
    event Action Started;

    /// <summary>Raised when the screensaver stops. The controller repaints the active page.</summary>
    event Action Stopped;
}
