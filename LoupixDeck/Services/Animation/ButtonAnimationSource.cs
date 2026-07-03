using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// The single per-device <see cref="IAnimationSource"/> that drives every animated button on the
/// active page (issue #121). It unifies two kinds of animated content:
///
/// <list type="bullet">
///   <item><b>Animated image layers</b> — an <see cref="ImageLayer"/> with an
///         <see cref="ImageLayer.AnimatedAssetPath"/>, played back from a pre-decoded
///         <see cref="DecodedAnimation"/> (no runtime ffmpeg).</item>
///   <item><b>Animated plugin commands</b> — an <c>IAnimatedDisplayCommand</c> surfaced as a
///         <see cref="RegisteredCommand"/> with <see cref="RegisteredCommand.IsAnimatedImageCommand"/>,
///         drawn onto a <see cref="PluginLayer"/>.</item>
/// </list>
///
/// The entry list is supplied by <see cref="ButtonAnimationManager"/> (which scans the page); the
/// source never scans pages itself. Each tick it advances the current frame of every entry from the
/// shared scheduler clock, dirty-checks so a button is only re-rendered+pushed when its frame
/// actually changed, and pushes each changed button directly via
/// <see cref="LoupedeckDevice.Device.LoupedeckDevice.DrawTouchButton"/> — a single-button partial
/// update, never a full-display redraw. The scheduler's per-source in-flight guard means a slow tick
/// just lowers the rate instead of piling up.
/// </summary>
public sealed class ButtonAnimationSource : IAnimationSource, IDisposable
{
    // Image animations are capped well below the global limit: the device is 90×90 and re-rendering
    // takes the global Skia gate (a known cross-device bottleneck), so a higher rate buys nothing
    // visible while costing contention. Plugins request their own rate (still globally clamped).
    private const int ImageFps = 15;

    private readonly IDeviceService _deviceService;
    private readonly LoupedeckConfig _config;
    private readonly IDeviceRouter _router;
    private readonly IServiceProvider _deviceProvider;

    // Signaled while no frame is being pushed. Dispose()/teardown waits on it so the serial port
    // is never closed mid framebuffer write (mirrors the screensaver source).
    private readonly System.Threading.ManualResetEventSlim _idle = new(true);

    private volatile Entry[] _entries = Array.Empty<Entry>();
    private volatile bool _enabled;

    public ButtonAnimationSource(
        IDeviceService deviceService,
        LoupedeckConfig config,
        IDeviceRouter router,
        IServiceProvider deviceProvider)
    {
        _deviceService = deviceService;
        _config = config;
        _router = router;
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

    /// <summary>Replaces the set of animated buttons (called by the manager on page change/rescan).</summary>
    public void SetEntries(IReadOnlyList<Entry> entries)
    {
        _entries = entries is { Count: > 0 } ? entries.ToArray() : Array.Empty<Entry>();
    }

    /// <summary>Pauses (false) or resumes (true) the whole source without unregistering it.</summary>
    public void SetEnabled(bool enabled) => _enabled = enabled;

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_enabled) return;

        var entries = _entries;
        if (entries.Length == 0) return;

        var device = _deviceService.Device;
        if (device == null) return;

        var columns = device.Columns;
        List<TouchButton> dirty = null;

        // Plugin render callbacks may call back into the host — make this device the ambient target
        // (issue #116 phase 2), exactly as the dynamic-text manager does.
        using (_router.Enter(_deviceProvider))
        {
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
                    var changed = entry switch
                    {
                        ImageEntry img => UpdateImageEntry(img, entryElapsed),
                        PluginEntry plugin => UpdatePluginEntry(plugin, entryElapsed, context),
                        _ => false
                    };

                    if (changed && entry.Button != null)
                        (dirty ??= new List<TouchButton>()).Add(entry.Button);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ButtonAnimationSource: entry render threw: {ex.Message}");
                }
            }
        }

        if (dirty == null) return;

        _idle.Reset();
        try
        {
            foreach (var button in dirty)
            {
                if (context.CancellationToken.IsCancellationRequested) return;
                await device.DrawTouchButton(button, _config, refresh: true, columns).ConfigureAwait(false);
            }
        }
        finally
        {
            _idle.Set();
        }
    }

    /// <summary>Advances an image entry; returns true when the displayed frame changed.</summary>
    private static bool UpdateImageEntry(ImageEntry entry, TimeSpan elapsed)
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

    /// <summary>Renders one plugin frame; returns true when a new frame was pushed.</summary>
    private bool UpdatePluginEntry(PluginEntry entry, TimeSpan elapsed, AnimationRenderContext context)
    {
        var render = entry.Command?.RenderAnimatedFrame;
        if (render == null) return false;

        var frameCtx = new AnimationFrameContext
        {
            FrameNumber = entry.FrameCounter,
            Elapsed = elapsed,
            Delta = context.Delta,
            EffectiveFps = context.EffectiveFps
        };

        var bitmap = new SKBitmap(90, 90);
        AnimationFrameInfo info;
        try
        {
            // Serialize with all other Skia work (font/glyph caches + the gated layer swap).
            lock (SkiaRenderGate.Sync)
            {
                using var canvas = new SKCanvas(bitmap);
                var rc = new SkiaRenderCanvas(canvas, 90, 90);
                info = render(entry.Parameters, entry.SequenceCommands, rc, frameCtx);
                if (info.Drawn) canvas.Flush();
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        entry.FrameCounter++;

        if (!info.Drawn)
        {
            // Plugin had nothing new this tick.
            bitmap.Dispose();
            if (info.IsFinal) entry.Finished = true;
            return false;
        }

        // Dirty-check on the plugin's own frame number: identical frame ⇒ no device push.
        if (info.FrameNumber == entry.LastPushedFrame)
        {
            bitmap.Dispose();
            if (info.IsFinal) entry.Finished = true;
            return false;
        }

        entry.Layer?.SetAnimationBitmap(bitmap);
        entry.LastPushedFrame = info.FrameNumber;
        if (info.IsFinal) entry.Finished = true;
        return true;
    }

    public void Dispose()
    {
        _enabled = false;
        try { _idle.Wait(1000); } catch { /* ignore */ }
        try { _idle.Dispose(); } catch { /* ignore */ }
    }

    // ── entry types ───────────────────────────────────────────────────────────

    /// <summary>One animated button on the active page. Created by the manager.</summary>
    public abstract class Entry
    {
        public TouchButton Button { get; init; }

        /// <summary>Desired frame rate this entry contributes to the source's <see cref="TargetFps"/>.</summary>
        public int DesiredFps { get; init; }

        /// <summary>Scheduler-clock offset captured when this entry first rendered (frame-0 anchor).</summary>
        public TimeSpan StartElapsed { get; set; }

        /// <summary>Last frame index/number actually pushed; <see cref="long.MinValue"/> = never.</summary>
        public long LastPushedFrame { get; set; } = long.MinValue;

        /// <summary>True once a one-shot animation has shown its final frame (no more ticks).</summary>
        public bool Finished { get; set; }
    }

    public sealed class ImageEntry : Entry
    {
        public required ImageLayer Layer { get; init; }
        public required DecodedAnimation Anim { get; init; }
    }

    public sealed class PluginEntry : Entry
    {
        public required RegisteredCommand Command { get; init; }
        public required string[] Parameters { get; init; }

        /// <summary>The button's full command sequence, forwarded to the plugin so it can compose
        /// from its siblings. Empty for single-command buttons.</summary>
        public IReadOnlyList<SequenceCommand> SequenceCommands { get; init; } = [];

        public required string OwnerKey { get; init; }

        /// <summary>The command's owner-keyed plugin layer, resolved by the manager on the UI thread
        /// (collection mutation is editor-bound, so it must not happen in the render loop).</summary>
        public required PluginLayer Layer { get; init; }

        /// <summary>Per-entry monotonic frame counter handed to the plugin.</summary>
        public long FrameCounter { get; set; }
    }
}
