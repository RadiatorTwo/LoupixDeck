using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Plays a video clip as the page wallpaper: the decoded frame fills the panel and the page's
/// keys are composited on top of it, every frame, in a single atomic full-panel write.
///
/// It is the screensaver's transfer path with content on top. The clip comes from the shared
/// <see cref="VideoFrameStream"/> (ffmpeg, realtime pacing, drop-to-freshest, and a seamless RAM
/// ring for a short loop), and <see cref="FullDisplayFrameWriter"/> pushes it — with this class's
/// overlay drawing the dim, any still side-slot images and the keys onto the frame before it is
/// sliced per display. Compositing before the slice is what keeps it to one write: a video frame
/// pushed first and keys written after would tear.
///
/// The overlay runs inside the shared Skia gate and on the frame budget, which at 30 fps is what
/// remains of ~33 ms after the ~27 ms the serial panel write costs. So it only blits: the keys come
/// pre-rendered from <see cref="TouchButtonForegroundCache"/>, never re-rendered here.
/// </summary>
public sealed class WallpaperAnimationSource : IAnimationSource, IDisposable
{
    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;
    private readonly LoupedeckConfig _config;
    private readonly DeviceGeometry _geometry;
    private readonly int _fps;

    // The slot this clip plays in, which receives each pushed frame as its paused still. Swapped
    // by Retarget and read under SkiaRenderGate.Sync.
    private WallpaperSlot _slot;

    // Asked live before every push, independent of SetEnabled: the manager hears about a takeover
    // only once its event reaches it, which may be after the new owner already painted.
    private readonly Func<bool> _mayOwnDisplay;

    // The page-name tile on a key, read under the Skia gate while composing a frame.
    private readonly Func<int, SKBitmap> _temporaryTile;

    private readonly VideoFrameStream _stream;
    private readonly FullDisplayFrameWriter _writer;
    private readonly TouchButtonForegroundCache _foregrounds;

    // Panel-space rectangle each key covers, resolved once: it comes from the device's key
    // calibration (which may leave gaps), not from index * keySize.
    private readonly SKRectI[] _keyRects;

    // The side strips' content without their background, left at 0 and right at 1, owned by this
    // source. The controller renders them (plugin calls cannot run on the frame budget) and hands
    // them over; they are swapped and read under SkiaRenderGate.Sync, like the key foregrounds.
    private readonly SKBitmap[] _strips = new SKBitmap[2];

    private volatile bool _active;
    private volatile bool _enabled = true;
    private bool _disposed;

    // 1 while the panel may not show the current overlay: set by every invalidation and when the
    // last push did not reach the panel, cleared right before a frame is composed. While it is 0,
    // a repeated ring frame would push the same picture again and is skipped.
    private int _overlayDirty = 1;

    // Diagnostics, opt-in via LOUPIX_WP_DEBUG=1: mirrors the screensaver's breakdown and adds the
    // overlay's own cost, which is the number that decides whether a given panel can carry this.
    private static readonly bool _debug =
        Environment.GetEnvironmentVariable("LOUPIX_WP_DEBUG") is "1" or "true" or "True";
    private const int DebugReportEvery = 30;
    private long _dbgFrames;
    private double _dbgOverlayMs;
    private double _dbgPushMs;
    private long _dbgDropped;
    private long _dbgWindowStart;

    public WallpaperAnimationSource(
        LoupedeckDevice.Device.LoupedeckDevice device,
        LoupedeckConfig config,
        WallpaperSlot slot,
        Func<bool> mayOwnDisplay,
        Func<int, SKBitmap> temporaryTile,
        string absoluteVideoPath,
        int fps,
        BitmapHelper.ScalingOption scaling)
    {
        _device = device;
        _config = config;
        _slot = slot;
        _mayOwnDisplay = mayOwnDisplay;
        _temporaryTile = temporaryTile;
        _geometry = device.Geometry;
        _fps = Math.Clamp(fps <= 0 ? 30 : fps, 1, 120);

        _stream = new VideoFrameStream(absoluteVideoPath, _geometry.PanelWidth, _geometry.PanelHeight,
            _fps, loop: true, _debug, "[Wallpaper]", scaling);
        _writer = new FullDisplayFrameWriter(device, _debug, "[Wallpaper]");

        var keySize = device.KeyCalibration.KeySize;
        _foregrounds = new TouchButtonForegroundCache(keySize, keySize, () => BitmapHelper.ShowsFolderBadges(config));

        var keyCount = Math.Max(0, device.Columns * device.Rows);
        _keyRects = new SKRectI[keyCount];
        for (var i = 0; i < keyCount; i++) _keyRects[i] = device.GetWallpaperKeyRect(i);
    }

    public int TargetFps => _fps;
    public bool IsActive => _active && _enabled;

    /// <summary>The device this clip plays on.</summary>
    public LoupedeckDevice.Device.LoupedeckDevice Device => _device;

    /// <summary>False once the frame stream ended (ffmpeg died); paused playback still runs.</summary>
    public bool IsRunning => _active;

    /// <summary>
    /// Moves playback to another slot playing the same clip — another page with the same
    /// wallpaper — so the clip keeps running instead of restarting ffmpeg and refilling its ring.
    /// </summary>
    public void Retarget(WallpaperSlot slot)
    {
        lock (SkiaRenderGate.Sync)
        {
            _slot = slot;
        }
    }

    /// <summary>
    /// Starts decoding. Returns false without starting anything when the device has no drawable
    /// display or ffmpeg can't be launched, so the caller can fall back to the still wallpaper.
    /// </summary>
    public bool Start()
    {
        if (_writer.TargetCount == 0)
        {
            Console.WriteLine("[Wallpaper] no drawable display on this device.");
            return false;
        }

        if (!_stream.Start()) return false;

        _dbgWindowStart = Stopwatch.GetTimestamp();
        _active = true;
        return true;
    }

    /// <summary>
    /// Copies the frame just pushed into the slot's <see cref="WallpaperSlot.VideoStill"/>, so keys
    /// rendered while playback is paused draw on what the panel was showing.
    /// </summary>
    private void KeepAsStill(byte[] bgra)
    {
        lock (SkiaRenderGate.Sync)
        {
            if (_disposed) return;

            SKBitmap still = _slot.VideoStill;
            if (still == null || still.Width != _geometry.PanelWidth || still.Height != _geometry.PanelHeight)
            {
                still = new SKBitmap(new SKImageInfo(_geometry.PanelWidth, _geometry.PanelHeight,
                    SKColorType.Bgra8888, SKAlphaType.Opaque));
                _slot.VideoStill = still;
            }

            Marshal.Copy(bgra, 0, still.GetPixels(), _geometry.FrameBytes);
        }
    }

    /// <summary>Pauses or resumes without tearing down ffmpeg — what the screensaver, a plugin
    /// takeover, folder navigation and page swipes need while they own the display.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        // Whoever owned the display meanwhile painted over the last frame.
        if (enabled) MarkOverlayDirty();
    }

    /// <summary>
    /// Makes the next frame go out even when the clip is still on the same frame — for anything
    /// the overlay draws that changed without an invalidation (the page-name tile, an opacity).
    /// </summary>
    public void MarkOverlayDirty() => Volatile.Write(ref _overlayDirty, 1);

    /// <summary>Re-renders one key on its next frame.</summary>
    public void InvalidateButton(int index)
    {
        _foregrounds.Invalidate(index);
        MarkOverlayDirty();
    }

    /// <summary>Re-renders every key on the next frame — what a page change needs.</summary>
    public void InvalidateAllButtons()
    {
        _foregrounds.InvalidateAll();
        MarkOverlayDirty();
    }

    /// <summary>
    /// Replaces one side strip's content, drawn over its column from the next frame on. Takes
    /// ownership of <paramref name="strip"/>, also when the source is already disposed.
    /// </summary>
    public void SetStrip(RotarySide side, SKBitmap strip)
    {
        var idx = side == RotarySide.Right ? 1 : 0;

        lock (SkiaRenderGate.Sync)
        {
            if (_disposed)
            {
                strip?.Dispose();
                return;
            }

            _strips[idx]?.Dispose();
            _strips[idx] = strip;
        }

        MarkOverlayDirty();
    }

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        // The live check is asked here too, not only before the push: while the device is off or
        // taken over, not even pulling a frame keeps ffmpeg from decoding ahead for nothing.
        if (!IsActive) return;
        if (!_mayOwnDisplay())
        {
            // Whoever owns the display now draws over the last frame.
            MarkOverlayDirty();
            return;
        }

        var token = _stream.Token ?? context.CancellationToken;

        var frame = await _stream.ReadFreshestAsync(token).ConfigureAwait(false);
        if (frame.Ended)
        {
            // Looping, so this only happens if ffmpeg died. Stop asking for frames rather than
            // spinning on a dead pipe; the manager notices via IsActive and restores the still
            // wallpaper.
            _active = false;
            Console.WriteLine("[Wallpaper] frame stream ended — playback stopped.");
            return;
        }

        if (!frame.HasFrame) return; // stopped / disposed

        // The panel already shows this ring frame with the current overlay: the clock has not
        // reached the next frame yet, or the clip is a single still. Pushing it again would only
        // cost a full serial write.
        if (frame.Repeat && Volatile.Read(ref _overlayDirty) == 0)
        {
            _stream.Release(frame);
            return;
        }

        // Cleared before composing, so an invalidation landing mid-compose sends the next frame.
        Volatile.Write(ref _overlayDirty, 0);
        var pushed = false;

        try
        {
            var overlayMs = 0d;
            var pushStart = _debug ? Stopwatch.GetTimestamp() : 0;

            // Asked again right before composing: the display may have been taken over (a folder
            // opened) while this frame was being waited for, and a frame sent now would land over
            // the new owner's first picture.
            pushed = await _writer.PushAsync(frame.Buffer, token, canvas =>
            {
                var overlayStart = _debug ? Stopwatch.GetTimestamp() : 0;
                DrawOverlay(canvas);
                if (_debug) overlayMs = Stopwatch.GetElapsedTime(overlayStart).TotalMilliseconds;
            }, () => _enabled && _mayOwnDisplay()).ConfigureAwait(false);

            if (pushed) KeepAsStill(frame.Buffer);

            if (!_debug) return;

            var pushMs = Stopwatch.GetElapsedTime(pushStart).TotalMilliseconds;
            _dbgOverlayMs += overlayMs;
            _dbgPushMs += pushMs;
            _dbgDropped += frame.Dropped;
            if (++_dbgFrames >= DebugReportEvery)
            {
                var windowMs = Stopwatch.GetElapsedTime(_dbgWindowStart).TotalMilliseconds;
                var effFps = windowMs > 0 ? _dbgFrames * 1000.0 / windowMs : 0;
                Console.WriteLine(
                    $"[Wallpaper][perf] {_dbgFrames} frames | overlay avg {_dbgOverlayMs / _dbgFrames:F1} ms | " +
                    $"push+overlay avg {_dbgPushMs / _dbgFrames:F1} ms | dropped {_dbgDropped} | " +
                    $"effective {effFps:F1} fps (target {_fps})");
                _dbgFrames = 0;
                _dbgOverlayMs = 0;
                _dbgPushMs = 0;
                _dbgDropped = 0;
                _dbgWindowStart = Stopwatch.GetTimestamp();
            }
        }
        finally
        {
            // Not on the panel (vetoed by a takeover, or the write failed): the next frame goes out.
            if (!pushed) MarkOverlayDirty();
            _stream.Release(frame);
        }
    }

    /// <summary>
    /// Draws everything that sits above the video frame, in the order the static wallpaper stacks
    /// it: the dim, then a still side-slot image where one is set, then the keys.
    ///
    /// Runs inside the shared Skia gate, on the render thread, on the frame budget — blits only.
    /// </summary>
    private void DrawOverlay(SKCanvas canvas)
    {
        var page = _config?.CurrentTouchButtonPage;
        if (page == null) return;

        // The dim covers the whole panel rather than only the key rectangles. The static path dims
        // each key's cutout as it renders it, which leaves a gapped grid's gaps undimmed — with a
        // still image nothing else is drawn there anyway, but a video frame fills the panel edge to
        // edge, so per-key dimming would leave bright seams between the keys.
        var opacity = page.MainWallpaper?.Opacity ?? 0;
        if (opacity > 0)
        {
            using var dim = new SKPaint
            {
                Color = new SKColor(0, 0, 0, (byte)(255 * opacity))
            };
            canvas.DrawRect(new SKRect(0, 0, _geometry.PanelWidth, _geometry.PanelHeight), dim);
        }

        var rightColumnX = _geometry.PanelWidth - _geometry.StripWidth;
        DrawSideSlot(canvas, page.LeftWallpaper, 0);
        DrawSideSlot(canvas, page.RightWallpaper, rightColumnX);

        DrawStrip(canvas, _strips[0], 0);
        DrawStrip(canvas, _strips[1], rightColumnX);

        var buttons = page.TouchButtons;
        if (buttons == null) return;

        foreach (var button in buttons)
        {
            if (button == null) continue;
            if ((uint)button.Index >= (uint)_keyRects.Length) continue;

            var rect = _keyRects[button.Index];
            var destination = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

            if (button.BackgroundEnabled)
            {
                // An explicit background hides the wallpaper behind this key, video included —
                // the same rule the static path applies.
                using var back = new SKPaint { Color = button.BackColor.ToSKColor() };
                canvas.DrawRect(destination, back);
            }

            // The page-name tile covers the key while it is shown.
            var foreground = _temporaryTile(button.Index) ?? _foregrounds.Get(button);
            if (foreground == null) continue;

            canvas.DrawBitmap(foreground, new SKRect(0, 0, foreground.Width, foreground.Height),
                destination, SKSamplingOptions.Default, paint: null);
        }
    }

    /// <summary>
    /// Draws a side display's own still image over its column of the panel, dimmed by that slot's
    /// opacity. A side slot without an image is left alone, so the video shows through it — the
    /// same precedence the static path has, where an empty side slot falls back to the main
    /// wallpaper's region.
    /// </summary>
    private void DrawSideSlot(SKCanvas canvas, WallpaperSlot slot, int columnX)
    {
        if (slot is not { HasImage: true }) return;

        var baked = BitmapHelper.GetOrBakeSlot(slot, _geometry.StripWidth, _geometry.PanelHeight);
        if (baked == null) return;

        var destination = new SKRect(columnX, 0, columnX + _geometry.StripWidth, _geometry.PanelHeight);
        canvas.DrawBitmap(baked, new SKRect(0, 0, baked.Width, baked.Height), destination,
            SKSamplingOptions.Default, paint: null);

        if (slot.Opacity <= 0) return;

        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 * slot.Opacity)) };
        canvas.DrawRect(destination, dim);
    }

    /// <summary>
    /// Draws a side strip's content over its column, above the video and any still side-slot
    /// image. The strip has no background of its own, so whatever is beneath shows through.
    /// </summary>
    private void DrawStrip(SKCanvas canvas, SKBitmap strip, int columnX)
    {
        if (strip == null) return;

        var destination = new SKRect(columnX, 0, columnX + _geometry.StripWidth, _geometry.PanelHeight);
        canvas.DrawBitmap(strip, new SKRect(0, 0, strip.Width, strip.Height), destination,
            SKSamplingOptions.Default, paint: null);
    }

    public void Dispose()
    {
        _active = false;
        // Stop decoding first, then let the writer drain so the serial port is never closed
        // mid-write, then free the cached key and strip bitmaps.
        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _writer.Dispose(); } catch { /* ignore */ }
        try { _foregrounds.Dispose(); } catch { /* ignore */ }

        lock (SkiaRenderGate.Sync)
        {
            _disposed = true;
            for (var i = 0; i < _strips.Length; i++)
            {
                _strips[i]?.Dispose();
                _strips[i] = null;
            }
        }
    }
}
