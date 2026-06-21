namespace LoupixDeck.Services.Animation;

/// <summary>
/// Per-frame timing snapshot handed to an <see cref="IAnimationSource"/> when the
/// central scheduler ticks it. All times are measured against a single shared
/// high-resolution clock so every animation on the device sees a consistent timebase.
/// </summary>
public readonly struct AnimationRenderContext(
    long frameNumber,
    TimeSpan elapsed,
    TimeSpan delta,
    int effectiveFps,
    CancellationToken cancellationToken)
{

    /// <summary>Zero-based frame counter for this source since it was registered.</summary>
    public long FrameNumber { get; } = frameNumber;

    /// <summary>Total time the source has been registered and ticking.</summary>
    public TimeSpan Elapsed { get; } = elapsed;

    /// <summary>Wall-clock time since this source's previous frame (the first frame
    /// reports the time since registration).</summary>
    public TimeSpan Delta { get; } = delta;

    /// <summary>The frame rate the scheduler is actually driving this source at,
    /// after clamping the source's <see cref="IAnimationSource.TargetFps"/> to the
    /// global limit. Useful for sources that pace their own interpolation.</summary>
    public int EffectiveFps { get; } = effectiveFps;

    /// <summary>Cancelled when the scheduler stops or the source is unregistered.
    /// A long-running render should observe this to bail out promptly.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
