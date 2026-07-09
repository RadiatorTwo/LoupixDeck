using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// The single per-device <see cref="IAnimationSource"/> that drives animated content on the two
/// side-strip displays (issue #123). It mirrors <see cref="ButtonAnimationSource"/> but targets the
/// 60×270 side strips: a FreeDraw strip's <see cref="RotaryButtonPage.StripCanvas"/> is itself a
/// <see cref="TouchButton"/> whose <see cref="ImageLayer"/> already supports an
/// <see cref="ImageLayer.AnimatedAssetPath"/>, so an animated image played back from a pre-decoded
/// <see cref="DecodedAnimation"/> renders on a strip exactly like a static image does.
///
/// The entry list is supplied by <see cref="SideDisplayAnimationManager"/> (which scans the current
/// left/right rotary pages); the source never scans pages itself. Each tick it advances the current
/// frame of every entry from the shared scheduler clock, dirty-checks so a strip is only re-rendered
/// when one of its layers' frames actually changed, and pushes each changed <em>side</em> once via
/// <see cref="IDeviceController.RefreshSideStripAnimationFrame"/> — the gated, rate-limited strip push
/// that reads the layer's current frame and skips while a swipe owns the strip.
/// </summary>
public sealed class SideDisplayAnimationSource : IAnimationSource, IDisposable
{
    // Image animations are capped well below the global limit for the same reason as buttons:
    // re-rendering a strip takes the global Skia gate, so a higher rate buys nothing visible while
    // costing contention. The per-side StripMinRedrawMs floor in the controller caps pushes anyway.
    private const int ImageFps = 15;

    private readonly IServiceProvider _deviceProvider;

    // Signaled while no frame is being pushed. Dispose()/teardown waits on it so the serial port is
    // never closed mid framebuffer write (mirrors ButtonAnimationSource / the screensaver source).
    private readonly System.Threading.ManualResetEventSlim _idle = new(true);

    private volatile SideEntry[] _entries = Array.Empty<SideEntry>();
    private volatile bool _enabled;

    public SideDisplayAnimationSource(IServiceProvider deviceProvider)
    {
        _deviceProvider = deviceProvider;
    }

    public int TargetFps
    {
        get
        {
            var entries = _entries;
            var fps = 0;
            foreach (var e in entries)
            {
                if (e.Finished) continue;
                if (e.DesiredFps > fps) fps = e.DesiredFps;
            }

            return fps; // 0 ⇒ let the scheduler use its global limit
        }
    }

    public bool IsActive
    {
        get
        {
            if (!_enabled) return false;
            foreach (var e in _entries)
                if (!e.Finished) return true;
            return false;
        }
    }

    /// <summary>Replaces the set of animated strip layers (called by the manager on page change/rescan).</summary>
    public void SetEntries(IReadOnlyList<SideEntry> entries)
    {
        _entries = entries is { Count: > 0 } ? entries.ToArray() : Array.Empty<SideEntry>();
    }

    /// <summary>Pauses (false) or resumes (true) the whole source without unregistering it.</summary>
    public void SetEnabled(bool enabled) => _enabled = enabled;

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_enabled) return;

        var entries = _entries;
        if (entries.Length == 0) return;

        // A strip is a single 60×270 push, so any changed layer on a side marks that side dirty once.
        var dirtyLeft = false;
        var dirtyRight = false;

        foreach (var entry in entries)
        {
            if (entry.Finished) continue;

            // First time this entry renders: anchor its timeline to "now" so it starts at frame 0
            // even though the shared scheduler clock has been running since the source registered.
            if (entry.LastPushedFrame == long.MinValue && entry.StartElapsed == TimeSpan.Zero)
                entry.StartElapsed = context.Elapsed;

            var entryElapsed = context.Elapsed - entry.StartElapsed;
            if (entryElapsed < TimeSpan.Zero) entryElapsed = TimeSpan.Zero;

            try
            {
                if (UpdateImageEntry(entry, entryElapsed))
                {
                    if (entry.Side == RotarySide.Right) dirtyRight = true;
                    else dirtyLeft = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SideDisplayAnimationSource: entry render threw: {ex.Message}");
            }
        }

        if (!dirtyLeft && !dirtyRight) return;

        var controller = _deviceProvider.GetService<IDeviceController>();
        if (controller == null) return;

        _idle.Reset();
        try
        {
            if (dirtyLeft && !context.CancellationToken.IsCancellationRequested)
                await controller.RefreshSideStripAnimationFrame(RotarySide.Left).ConfigureAwait(false);

            if (dirtyRight && !context.CancellationToken.IsCancellationRequested)
                await controller.RefreshSideStripAnimationFrame(RotarySide.Right).ConfigureAwait(false);
        }
        finally
        {
            _idle.Set();
        }
    }

    /// <summary>Advances an image entry; returns true when the displayed frame changed.</summary>
    private static bool UpdateImageEntry(SideEntry entry, TimeSpan elapsed)
    {
        var anim = entry.Anim;
        if (anim == null || anim.Frames.Length == 0) return false;

        var index = anim.FrameIndexAt(elapsed);
        if (index == entry.LastPushedFrame) return false;

        entry.Layer.SetAnimationFrame(anim.Frames[index]);
        entry.LastPushedFrame = index;

        // One-shot (non-looping) image: stop once the last frame is shown.
        if (!anim.Loops && index >= anim.Frames.Length - 1)
            entry.Finished = true;

        return true;
    }

    public void Dispose()
    {
        _enabled = false;
        try { _idle.Wait(1000); } catch { /* ignore */ }
        try { _idle.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// One animated image layer on a side strip's FreeDraw canvas. Created by the manager. Kept as a
    /// dedicated type (rather than reusing the button entry) so a future plugin-driven animated-strip
    /// entry can slot in alongside it without reshaping the source.
    /// </summary>
    public sealed class SideEntry
    {
        /// <summary>Which strip this layer belongs to (never <see cref="RotarySide.Both"/>).</summary>
        public required RotarySide Side { get; init; }

        public required ImageLayer Layer { get; init; }
        public required DecodedAnimation Anim { get; init; }

        /// <summary>Desired frame rate this entry contributes to the source's <see cref="TargetFps"/>.</summary>
        public int DesiredFps { get; init; }

        /// <summary>Scheduler-clock offset captured when this entry first rendered (frame-0 anchor).</summary>
        public TimeSpan StartElapsed { get; set; }

        /// <summary>Last frame index actually pushed; <see cref="long.MinValue"/> = never.</summary>
        public long LastPushedFrame { get; set; } = long.MinValue;

        /// <summary>True once a one-shot animation has shown its final frame (no more ticks).</summary>
        public bool Finished { get; set; }
    }
}
