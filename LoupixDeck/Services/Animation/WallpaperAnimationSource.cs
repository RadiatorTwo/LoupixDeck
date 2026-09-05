using System.Diagnostics;
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

    private readonly VideoFrameStream _stream;
    private readonly FullDisplayFrameWriter _writer;
    private readonly TouchButtonForegroundCache _foregrounds;

    // Panel-space rectangle each key covers, resolved once: it comes from the device's key
    // calibration (which may leave gaps), not from index * keySize.
    private readonly SKRectI[] _keyRects;

    private volatile bool _active;
    private volatile bool _enabled = true;

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
        string absoluteVideoPath,
        int fps)
    {
        _device = device;
        _config = config;
        _geometry = device.Geometry;
        _fps = Math.Clamp(fps <= 0 ? 30 : fps, 1, 120);

        _stream = new VideoFrameStream(absoluteVideoPath, _geometry.PanelWidth, _geometry.PanelHeight,
            _fps, loop: true, _debug, "[Wallpaper]");
        _writer = new FullDisplayFrameWriter(device, _debug, "[Wallpaper]");

        var keySize = device.KeyCalibration.KeySize;
        _foregrounds = new TouchButtonForegroundCache(keySize, keySize);

        var keyCount = Math.Max(0, device.Columns * device.Rows);
        _keyRects = new SKRectI[keyCount];
        for (var i = 0; i < keyCount; i++) _keyRects[i] = device.GetWallpaperKeyRect(i);
    }

    public int TargetFps => _fps;
    public bool IsActive => _active && _enabled;

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

    /// <summary>Pauses or resumes without tearing down ffmpeg — what the screensaver, a plugin
    /// takeover, folder navigation and page swipes need while they own the display.</summary>
    public void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>Re-renders one key on its next frame.</summary>
    public void InvalidateButton(int index) => _foregrounds.Invalidate(index);

    /// <summary>Re-renders every key on the next frame — what a page change needs.</summary>
    public void InvalidateAllButtons() => _foregrounds.InvalidateAll();

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!IsActive) return;

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

        try
        {
            var overlayMs = 0d;
            var pushStart = _debug ? Stopwatch.GetTimestamp() : 0;

            await _writer.PushAsync(frame.Buffer, token, canvas =>
            {
                var overlayStart = _debug ? Stopwatch.GetTimestamp() : 0;
                DrawOverlay(canvas);
                if (_debug) overlayMs = Stopwatch.GetElapsedTime(overlayStart).TotalMilliseconds;
            }).ConfigureAwait(false);

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

        DrawSideSlot(canvas, page.LeftWallpaper, 0);
        DrawSideSlot(canvas, page.RightWallpaper, _geometry.PanelWidth - _geometry.StripWidth);

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

            var foreground = _foregrounds.Get(button);
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

    public void Dispose()
    {
        _active = false;
        // Stop decoding first, then let the writer drain so the serial port is never closed
        // mid-write, then free the cached key bitmaps.
        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _writer.Dispose(); } catch { /* ignore */ }
        try { _foregrounds.Dispose(); } catch { /* ignore */ }
    }
}
