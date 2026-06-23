namespace LoupixDeck.Services.Animation;

/// <summary>
/// One animated thing the central scheduler drives — a per-button effect, a
/// full-display screensaver, a side-strip transition, a plugin renderer, etc.
/// </summary>
/// <remarks>
/// <para>
/// A source never owns a timer or render loop of its own: it registers with the
/// <see cref="IAnimationScheduler"/>, declares the rate it would like via
/// <see cref="TargetFps"/>, and gates whether it currently wants frames via
/// <see cref="IsActive"/>. The scheduler calls <see cref="RenderFrameAsync"/> on a
/// background thread at the (globally clamped) cadence while the source is active.
/// </para>
/// <para>
/// Sources that only change occasionally should keep returning quickly when nothing
/// changed (dirty-state lives in the source) and/or flip <see cref="IsActive"/> to
/// <c>false</c> when idle so the scheduler stops ticking them entirely.
/// </para>
/// </remarks>
public interface IAnimationSource
{
    /// <summary>Desired frame rate. A value &lt;= 0 means "use the scheduler's global
    /// limit". The scheduler always clamps the effective rate to the global limit.</summary>
    int TargetFps { get; }

    /// <summary>Whether the source currently wants to be driven. Polled by the scheduler
    /// every tick, so flipping it to <c>false</c> (e.g. when the owning page is no longer
    /// the active page) pauses rendering without unregistering. Flip it back to <c>true</c>
    /// and call <see cref="IAnimationScheduler.RequestFrame"/> to resume immediately.</summary>
    bool IsActive { get; }

    /// <summary>Renders one frame. Invoked on a background thread; Skia work must still go
    /// through <see cref="LoupixDeck.Utils.SkiaRenderGate.Sync"/> and UI-bound mutations must
    /// be posted to the dispatcher, exactly as the rest of the device pipeline does. The
    /// scheduler never starts a new frame for this source while the previous one is still
    /// running, so a slow render simply lowers this source's effective rate instead of
    /// piling up.</summary>
    Task RenderFrameAsync(AnimationRenderContext context);
}
