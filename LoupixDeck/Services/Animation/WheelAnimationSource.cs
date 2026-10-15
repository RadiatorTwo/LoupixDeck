using LoupixDeck.Controllers;
using LoupixDeck.Models.Layers;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// The per-device <see cref="IAnimationSource"/> that drives animated image layers on the active
/// wheel mode's canvas (issue #394). It mirrors <see cref="SideDisplayAnimationSource"/>: the entry
/// list comes from <see cref="WheelAnimationManager"/>, each tick advances the current frame of every
/// entry from the shared scheduler clock, and a changed frame is pushed once through
/// <see cref="IDeviceController.RefreshWheelAnimationFrame"/>, the gated push that shares the wheel's
/// redraw gate with value updates.
/// </summary>
public sealed class WheelAnimationSource(IServiceProvider deviceProvider) : IAnimationSource, IDisposable
{
    // Signaled while no frame is being pushed. Dispose() waits on it so the serial port is never
    // closed mid framebuffer write (same as the strip and button sources).
    private readonly System.Threading.ManualResetEventSlim _idle = new(true);

    private volatile WheelEntry[] _entries = [];
    private volatile bool _enabled;

    public int TargetFps
    {
        get
        {
            int fps = 0;
            foreach (WheelEntry e in _entries)
            {
                if (e.Finished) continue;
                if (e.DesiredFps > fps) fps = e.DesiredFps;
            }

            return fps; // 0 => let the scheduler use its global limit
        }
    }

    public bool IsActive
    {
        get
        {
            if (!_enabled) return false;
            foreach (WheelEntry e in _entries)
                if (!e.Finished) return true;
            return false;
        }
    }

    /// <summary>Replaces the set of animated wheel layers (called by the manager on rescan).</summary>
    public void SetEntries(IReadOnlyList<WheelEntry> entries)
    {
        WheelEntry[] next = entries is { Count: > 0 } ? [.. entries] : [];

        // A rescan for an edit that left a layer's animation as it was keeps that layer's timeline,
        // so its first tick after the rescan is not taken for a new frame and does not repaint.
        WheelEntry[] previous = _entries;
        foreach (WheelEntry entry in next)
        {
            WheelEntry same = previous.FirstOrDefault(p =>
                ReferenceEquals(p.Layer, entry.Layer) && ReferenceEquals(p.Anim, entry.Anim));
            if (same == null) continue;

            entry.StartElapsed = same.StartElapsed;
            entry.LastPushedFrame = same.LastPushedFrame;
            entry.Finished = same.Finished;
        }

        _entries = next;
    }

    /// <summary>Pauses (false) or resumes (true) the whole source without unregistering it.</summary>
    public void SetEnabled(bool enabled) => _enabled = enabled;

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_enabled) return;

        WheelEntry[] entries = _entries;
        if (entries.Length == 0) return;

        IDeviceController controller = deviceProvider.GetService<IDeviceController>();
        if (controller == null || controller.IsDeviceOff) return;

        bool dirty = false;
        foreach (WheelEntry entry in entries)
        {
            if (entry.Finished) continue;

            // First time this entry renders: anchor its timeline to "now" so it starts at frame 0
            // even though the shared scheduler clock has been running since the source registered.
            if (entry.LastPushedFrame == long.MinValue && entry.StartElapsed == TimeSpan.Zero)
                entry.StartElapsed = context.Elapsed;

            TimeSpan entryElapsed = context.Elapsed - entry.StartElapsed;
            if (entryElapsed < TimeSpan.Zero) entryElapsed = TimeSpan.Zero;

            try
            {
                if (UpdateEntry(entry, entryElapsed))
                    dirty = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WheelAnimationSource: entry render threw: {ex.Message}");
            }
        }

        if (!dirty || context.CancellationToken.IsCancellationRequested) return;

        _idle.Reset();
        try
        {
            await controller.RefreshWheelCanvasFrame().ConfigureAwait(false);
        }
        finally
        {
            _idle.Set();
        }
    }

    /// <summary>Advances an entry; returns true when the displayed frame changed.</summary>
    private static bool UpdateEntry(WheelEntry entry, TimeSpan elapsed)
    {
        DecodedAnimation anim = entry.Anim;
        if (anim == null || anim.Frames.Length == 0) return false;

        int index = anim.FrameIndexAt(elapsed);
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

    /// <summary>One animated image layer on the active wheel mode's canvas. Created by the manager.</summary>
    public sealed class WheelEntry
    {
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
