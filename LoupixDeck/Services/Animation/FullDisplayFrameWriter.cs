using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Shared full-display frame push path. Takes one raw BGRA panel (the continuous virtual panel
/// the wallpaper system assumes, sized by the device's geometry) and fans it out across every display of a device: unified
/// devices (Live S / Razer) take the whole frame on their single "center" buffer; the CT's
/// independent left/centre/right buffers each take their column. The CT knob screen is intentionally
/// not driven (its framebuffer needs big-endian conversion the device layer doesn't implement yet).
///
/// Factored out of <see cref="ScreensaverAnimationSource"/> so both the built-in screensaver and
/// plugin-provided full-display renderers share one battle-tested transfer path: a single atomic
/// full-screen blit + DRAW per frame (the no-tearing path), the Skia gate composite, and an idle
/// gate so <see cref="Dispose"/> never cuts a serial framebuffer write mid-stream (which would
/// desync the device protocol until a power-cycle).
/// </summary>
public sealed class FullDisplayFrameWriter : IDisposable
{
    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;

    // The continuous virtual panel the wallpaper system assumes, taken from the device:
    // its full width spanning the centre grid plus any side-strip columns.
    private readonly DeviceGeometry _geometry;
    private readonly List<DisplayTarget> _targets = [];

    private readonly bool _debug;
    private readonly string _logPrefix;

    // Signaled while no frame is being pushed to the device. Dispose() waits on this so a
    // caller that closes the serial port right after (controller shutdown on app quit)
    // can't cut a full-screen framebuffer write mid-stream — that desyncs the device's
    // protocol and makes the next launch's handshake time out until a power-cycle.
    private readonly ManualResetEventSlim _idle = new(true);

    public FullDisplayFrameWriter(
        LoupedeckDevice.Device.LoupedeckDevice device,
        bool debug = false,
        string logPrefix = "[FullDisplay]")
    {
        _device = device;
        _geometry = device.Geometry;
        _debug = debug;
        _logPrefix = logPrefix;
        BuildTargets();
    }

    /// <summary>How many displays this writer will push to. Zero means the device has no drawable
    /// display and the caller should abort cleanly.</summary>
    public int TargetCount => _targets.Count;

    /// <summary>
    /// Composites the per-display slices under the shared Skia gate, then pushes each to its
    /// display outside the gate (the device's pixel conversion takes the gate itself, and it can't
    /// be held across the awaited device I/O). <paramref name="bgra"/> is exactly one panel frame
    /// (the geometry's FrameBytes are read; the buffer may be larger when pooled).
    /// </summary>
    public async Task PushAsync(byte[] bgra, CancellationToken token)
    {
        SKBitmap frame = null;
        var draws = new List<(string Id, SKBitmap Bitmap, bool Owned)>(_targets.Count);

        lock (SkiaRenderGate.Sync)
        {
            frame = new SKBitmap(new SKImageInfo(_geometry.PanelWidth, _geometry.PanelHeight,
                SKColorType.Bgra8888, SKAlphaType.Opaque));
            // Copy exactly one frame: the buffer may be pooled (ArrayPool) so it can be larger
            // than one frame — never use bgra.Length here.
            Marshal.Copy(bgra, 0, frame.GetPixels(), _geometry.FrameBytes);

            foreach (var target in _targets)
            {
                if (target.IsFullFrame)
                {
                    // The whole panel frame goes straight to the unified buffer.
                    draws.Add((target.DisplayId, frame, false));
                    continue;
                }

                var slice = new SKBitmap(new SKImageInfo(target.DestWidth, target.DestHeight,
                    SKColorType.Bgra8888, SKAlphaType.Opaque));
                using (var canvas = new SKCanvas(slice))
                {
                    canvas.DrawBitmap(frame, target.SrcRect,
                        new SKRect(0, 0, target.DestWidth, target.DestHeight),
                        SKSamplingOptions.Default, paint: null);
                }
                draws.Add((target.DisplayId, slice, true));
            }
        }

        _idle.Reset();
        try
        {
            foreach (var draw in draws)
            {
                if (token.IsCancellationRequested) return;
                // refresh:true — one atomic full-display FRAMEBUFF + DRAW per frame. A
                // framebuffer write WITHOUT a DRAW does not reliably present on the device
                // (the last DRAW'd page content stays visible), so the frame must be drawn.
                // A single full-screen blit + DRAW is the no-tearing path (same as
                // DrawTouchSlotsAtomic); only per-slot writes cause tearing.
                if (_debug)
                {
                    var ts = Stopwatch.GetTimestamp();
                    await _device.DrawScreen(draw.Id, draw.Bitmap, refresh: true).ConfigureAwait(false);
                    var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
                    // Flag slow draws (a multi-second value means the FRAMEBUFF/DRAW ACK is
                    // timing out, not just slow serial throughput).
                    if (ms > 500)
                        Console.WriteLine($"{_logPrefix}[perf] slow DrawScreen('{draw.Id}'): {ms:F0} ms");
                }
                else
                {
                    await _device.DrawScreen(draw.Id, draw.Bitmap, refresh: true).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (SkiaRenderGate.Sync)
            {
                foreach (var draw in draws)
                    if (draw.Owned) draw.Bitmap.Dispose();
                frame.Dispose();
            }
            _idle.Set();
        }
    }

    /// <summary>
    /// Builds the slice targets from the device's displays. A unified device exposes a single
    /// 480-wide "center" buffer (the Razer's side strips are columns of it); the CT exposes
    /// independent narrower buffers that each map to a column of the panel.
    /// </summary>
    private void BuildTargets()
    {
        var (centerW, centerH) = _device.GetDisplaySize("center");
        if (centerW <= 0 || centerH <= 0) return;

        if (centerW >= _geometry.PanelWidth)
        {
            // Unified panel: push the full frame as-is (covers grid + any side columns).
            _targets.Add(DisplayTarget.Full("center", centerW, centerH));
            return;
        }

        // Segmented displays (CT): slice the continuous panel into its columns.
        int stripWidth = _geometry.StripWidth;
        AddSlice("left", 0, stripWidth);
        AddSlice("center", stripWidth, centerW);
        AddSlice("right", _geometry.PanelWidth - stripWidth, stripWidth);
        // "knob" (240×240) is deliberately omitted — see class summary.
    }

    private void AddSlice(string displayId, int srcX, int srcWidth)
    {
        var (w, h) = _device.GetDisplaySize(displayId);
        if (w <= 0 || h <= 0) return;
        _targets.Add(DisplayTarget.Slice(displayId, srcX, srcWidth, w, h, _geometry.PanelHeight));
    }

    /// <summary>Waits for any in-flight frame push to finish so the caller can safely close the
    /// serial port without cutting a write mid-stream, then releases the idle gate.</summary>
    public void Dispose()
    {
        try { _idle.Wait(1000); } catch { /* ignore */ }
        try { _idle.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>One display's slice of the panel: which buffer, the source rectangle in the
    /// panel frame, and the destination size (the display's own pixels).</summary>
    private sealed class DisplayTarget
    {
        public string DisplayId { get; private init; }
        public bool IsFullFrame { get; private init; }
        public SKRect SrcRect { get; private init; }
        public int DestWidth { get; private init; }
        public int DestHeight { get; private init; }

        public static DisplayTarget Full(string id, int width, int height) => new()
        {
            DisplayId = id,
            IsFullFrame = true,
            SrcRect = new SKRect(0, 0, width, height),
            DestWidth = width,
            DestHeight = height
        };

        public static DisplayTarget Slice(string id, int srcX, int srcWidth, int destWidth, int destHeight,
            int panelHeight) => new()
        {
            DisplayId = id,
            IsFullFrame = false,
            SrcRect = new SKRect(srcX, 0, srcX + srcWidth, panelHeight),
            DestWidth = destWidth,
            DestHeight = destHeight
        };
    }
}
