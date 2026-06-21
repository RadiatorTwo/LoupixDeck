using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// An animated image decoded once into a flat array of full button-size frames plus their
/// per-frame durations. Produced by <see cref="LoupixDeck.Utils.AnimatedImageDecoder"/> and held by
/// <see cref="IAnimatedImageCache"/> so the same animation shared across buttons (and devices)
/// decodes a single time. Playback is one bitmap blit per frame — no per-frame ffmpeg, no delta
/// reconstruction at draw time.
/// </summary>
public sealed class DecodedAnimation : IDisposable
{
    /// <summary>Fully composited frames, one per animation frame (never null/empty).</summary>
    public SKBitmap[] Frames { get; }

    /// <summary>Display duration of each frame in milliseconds, aligned with <see cref="Frames"/>.</summary>
    public int[] DurationsMs { get; }

    /// <summary>Whether playback should loop (button animations loop by default).</summary>
    public bool Loops { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Sum of all frame durations; the loop period.</summary>
    public int TotalDurationMs { get; }

    /// <summary>Approximate native pixel memory held by all frames (for the cache budget).</summary>
    public long TotalBytes { get; }

    private bool _disposed;

    public DecodedAnimation(SKBitmap[] frames, int[] durationsMs, bool loops, int width, int height)
    {
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        DurationsMs = durationsMs ?? throw new ArgumentNullException(nameof(durationsMs));
        if (frames.Length == 0)
            throw new ArgumentException("Animation must have at least one frame.", nameof(frames));
        Loops = loops;
        Width = width;
        Height = height;

        var total = 0;
        foreach (var d in durationsMs) total += d;
        TotalDurationMs = total;

        long bytes = 0;
        foreach (var f in frames)
            if (f != null) bytes += (long)f.Width * f.Height * 4;
        TotalBytes = bytes;
    }

    /// <summary>
    /// Picks the frame to show at <paramref name="elapsed"/> from the start of playback by walking
    /// the cumulative per-frame durations, so content timing follows the source's own frame
    /// durations independently of the scheduler's tick rate. Loops modulo
    /// <see cref="TotalDurationMs"/>; a one-shot holds the last frame once finished.
    /// </summary>
    public int FrameIndexAt(TimeSpan elapsed)
    {
        if (Frames.Length <= 1 || TotalDurationMs <= 0) return 0;

        var ms = elapsed.TotalMilliseconds;
        if (Loops)
            ms %= TotalDurationMs;
        else if (ms >= TotalDurationMs)
            return Frames.Length - 1;

        var acc = 0;
        for (var i = 0; i < DurationsMs.Length; i++)
        {
            acc += DurationsMs[i];
            if (ms < acc) return i;
        }

        return Frames.Length - 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose native pixel memory under the shared gate so it never overlaps active Skia work.
        lock (SkiaRenderGate.Sync)
        {
            foreach (var f in Frames)
                f?.Dispose();
        }
    }
}
