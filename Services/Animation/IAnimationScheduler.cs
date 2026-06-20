namespace LoupixDeck.Services.Animation;

/// <summary>
/// The single animation loop for one device. Every animated feature (per-button
/// effects, full-display screensavers, side-strip transitions, plugin renderers)
/// registers here instead of spinning up its own timer, so the device has exactly
/// one render cadence that can be globally rate-limited, paused for inactive pages,
/// and optimised in one place.
/// </summary>
public interface IAnimationScheduler
{
    /// <summary>Adds a source to the loop. Idempotent — registering the same instance
    /// twice is a no-op. Starts the loop if it was idle.</summary>
    void Register(IAnimationSource source);

    /// <summary>Removes a source. Safe to call at any time, including from inside the
    /// source's own <see cref="IAnimationSource.RenderFrameAsync"/>. Any frame already
    /// in flight for the source is allowed to finish.</summary>
    void Unregister(IAnimationSource source);

    /// <summary>Upper bound on frames per second for every source on this device. A
    /// source's <see cref="IAnimationSource.TargetFps"/> is clamped to this value. Takes
    /// effect on the next tick. Values &lt;= 0 are ignored.</summary>
    void SetGlobalFpsLimit(int fps);

    /// <summary>The current global FPS limit.</summary>
    int GlobalFpsLimit { get; }

    /// <summary>Wakes the loop so it re-evaluates due times immediately — used when a
    /// source becomes active again, or has dirty content it wants pushed without waiting
    /// for its next scheduled frame. No-op if <paramref name="source"/> is not registered.</summary>
    void RequestFrame(IAnimationSource source);

    /// <summary>Stops the loop and cancels the frame-context token. Registered sources are
    /// kept, so a later <see cref="Register"/> / <see cref="RequestFrame"/> restarts it.</summary>
    void Stop();
}
