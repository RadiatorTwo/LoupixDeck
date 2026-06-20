using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Full-display animated screensaver source (issue #120). Decodes the configured clip
/// (GIF or any ffmpeg-supported video) into a stream of raw BGRA frames via an external
/// <c>ffmpeg</c> process and fans each frame out across every display of the device.
///
/// It is driven by the central <see cref="IAnimationScheduler"/> — the scheduler ticks
/// <see cref="RenderFrameAsync"/> at the configured rate, and because each tick blocks on
/// the next frame from the ffmpeg pipe, playback naturally paces to ffmpeg's constant
/// output rate (no <c>-re</c>; a single 480×270 frame is larger than the OS pipe buffer,
/// so ffmpeg is throttled by our read). The scheduler's in-flight guard means a slow read
/// simply lowers the effective rate instead of queueing frames.
///
/// The decode geometry mirrors the wallpaper system's continuous 480×270 panel: the frame
/// is decoded at panel size and sliced per display. Unified devices (Live S / Razer) take
/// the whole frame on their single buffer; the CT's independent left/centre/right buffers
/// each take their column. The CT knob screen is intentionally not driven (its framebuffer
/// needs big-endian conversion the device layer doesn't implement yet).
/// </summary>
public sealed class ScreensaverAnimationSource : IAnimationSource, IDisposable
{
    // The continuous virtual panel the wallpaper system assumes: 480px wide spanning the
    // centre grid plus both 60px side-strip columns, 270px tall.
    private const int PanelWidth = 480;
    private const int PanelHeight = 270;
    private const int StripWidth = 60;

    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;
    private readonly string _absoluteVideoPath;
    private readonly int _fps;
    private readonly bool _loop;
    private readonly Action _onEnded;

    private readonly List<DisplayTarget> _targets = [];

    private Process _ffmpeg;
    private Stream _stdout;
    private byte[] _frameBuffer;
    private CancellationTokenSource _cts;
    private volatile bool _active;
    private int _endedSignalled;
    private long _startTimestamp;
    private bool _firstFrameLogged;

    // Signaled while no frame is being pushed to the device. Dispose() waits on this so a
    // caller that closes the serial port right after (controller shutdown on app quit)
    // can't cut a full-screen framebuffer write mid-stream — that desyncs the device's
    // protocol and makes the next launch's handshake time out until a power-cycle.
    private readonly ManualResetEventSlim _idle = new(true);

    public ScreensaverAnimationSource(LoupedeckDevice.Device.LoupedeckDevice device,
        string absoluteVideoPath, int fps, bool loop, Action onEnded)
    {
        _device = device;
        _absoluteVideoPath = absoluteVideoPath;
        _fps = Math.Clamp(fps <= 0 ? 30 : fps, 1, 120);
        _loop = loop;
        _onEnded = onEnded;
    }

    public int TargetFps => _fps;
    public bool IsActive => _active;

    /// <summary>
    /// Launches ffmpeg and prepares the per-display slice targets. Returns false (and
    /// performs no partial start) when there is nothing to draw to or ffmpeg can't be
    /// started, so the caller can abort cleanly. Synchronous: only spawns the process.
    /// </summary>
    public bool Start()
    {
        BuildTargets();
        if (_targets.Count == 0)
        {
            Console.WriteLine("[Screensaver] no drawable display on this device.");
            return false;
        }

        _cts = new CancellationTokenSource();

        // Argument layout matters: global opts, then INPUT opts (before -i), then OUTPUT opts.
        //
        // Startup latency fix: by default ffmpeg analyses up to ~5 s of the input
        // (-analyzeduration) before emitting the first frame, so the screensaver appeared
        // to "hang" for seconds after the idle timeout. -analyzeduration 0 + a small
        // -probesize + -fflags nobuffer make it start decoding immediately.
        //
        // -stream_loop -1 loops the input forever (what a screensaver wants); no -re so the
        // consumer paces playback; -r gives constant-rate output so frame i == time i/fps.
        var loopArg = _loop ? "-stream_loop -1 " : string.Empty;
        var args =
            "-hide_banner -loglevel error " +
            "-fflags nobuffer -probesize 500000 -analyzeduration 0 " +
            $"{loopArg}-i \"{_absoluteVideoPath}\" " +
            $"-an -f rawvideo -r {_fps} -pix_fmt bgra -vf scale={PanelWidth}:{PanelHeight} -";

        try
        {
            _ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screensaver] ffmpeg start failed: {ex.Message}");
            return false;
        }

        if (_ffmpeg == null)
        {
            Console.WriteLine("[Screensaver] ffmpeg failed to start (is it on PATH?).");
            return false;
        }

        _stdout = _ffmpeg.StandardOutput.BaseStream;
        _frameBuffer = new byte[PanelWidth * PanelHeight * 4];
        _startTimestamp = Stopwatch.GetTimestamp();

        // Drain stderr continuously: ffmpeg logs progress there and stalls if the pipe fills.
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { _ = await _ffmpeg.StandardError.ReadToEndAsync(token); }
            catch { /* killed / cancelled */ }
        }, token);

        _active = true;
        return true;
    }

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_active) return;

        var stream = _stdout;
        var buffer = _frameBuffer;
        if (stream == null || buffer == null) return;

        var token = _cts?.Token ?? context.CancellationToken;

        // Read exactly one full panel frame. The blocking read is what paces us to ffmpeg.
        var read = 0;
        while (read < buffer.Length)
        {
            int r;
            try
            {
                r = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // stopped / disposed
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screensaver] frame read failed: {ex.Message}");
                SignalEnded();
                return;
            }

            if (r <= 0)
            {
                // End of stream: a non-looping clip finished, or ffmpeg exited.
                SignalEnded();
                return;
            }

            read += r;
        }

        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            var ms = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
            Console.WriteLine($"[Screensaver] first frame after {ms:F0} ms.");
        }

        await PushFrameAsync(buffer, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Composites the per-display slices under the shared Skia gate, then pushes each to
    /// its display outside the gate (the device's pixel conversion takes the gate itself,
    /// and it can't be held across the awaited device I/O).
    /// </summary>
    private async Task PushFrameAsync(byte[] bgra, CancellationToken token)
    {
        SKBitmap frame = null;
        var draws = new List<(string Id, SKBitmap Bitmap, bool Owned)>(_targets.Count);

        lock (SkiaRenderGate.Sync)
        {
            frame = new SKBitmap(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
            Marshal.Copy(bgra, 0, frame.GetPixels(), bgra.Length);

            foreach (var target in _targets)
            {
                if (target.IsFullFrame)
                {
                    // The whole 480×270 frame goes straight to the unified buffer.
                    draws.Add((target.DisplayId, frame, false));
                    continue;
                }

                var slice = new SKBitmap(new SKImageInfo(target.DestWidth, target.DestHeight,
                    SKColorType.Bgra8888, SKAlphaType.Opaque));
                using (var canvas = new SKCanvas(slice))
                {
                    canvas.DrawBitmap(frame, target.SrcRect,
                        new SKRect(0, 0, target.DestWidth, target.DestHeight));
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
                await _device.DrawScreen(draw.Id, draw.Bitmap, refresh: true).ConfigureAwait(false);
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
    /// Builds the slice targets from the device's displays. A unified device exposes a
    /// single 480-wide "center" buffer (the Razer's side strips are columns of it); the CT
    /// exposes independent narrower buffers that each map to a column of the panel.
    /// </summary>
    private void BuildTargets()
    {
        var (centerW, centerH) = _device.GetDisplaySize("center");
        if (centerW <= 0 || centerH <= 0) return;

        if (centerW >= PanelWidth)
        {
            // Unified panel: push the full frame as-is (covers grid + any side columns).
            _targets.Add(DisplayTarget.Full("center", centerW, centerH));
            return;
        }

        // Segmented displays (CT): slice the continuous panel into its columns.
        AddSlice("left", 0, StripWidth);
        AddSlice("center", StripWidth, centerW);
        AddSlice("right", PanelWidth - StripWidth, StripWidth);
        // "knob" (240×240) is deliberately omitted — see class summary.
    }

    private void AddSlice(string displayId, int srcX, int srcWidth)
    {
        var (w, h) = _device.GetDisplaySize(displayId);
        if (w <= 0 || h <= 0) return;
        _targets.Add(DisplayTarget.Slice(displayId, srcX, srcWidth, w, h));
    }

    private void SignalEnded()
    {
        _active = false;
        if (Interlocked.Exchange(ref _endedSignalled, 1) != 0) return;
        try { _onEnded?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"[Screensaver] onEnded handler threw: {ex.Message}"); }
    }

    public void Dispose()
    {
        _active = false;
        // Cancel the read (aborts a blocked ReadAsync) and stop ffmpeg first…
        try { _cts?.Cancel(); } catch { /* already disposed */ }
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { /* already gone */ }
        // …then wait for any frame that is currently being drawn to the device to finish,
        // so the caller can safely close the serial port without cutting a write mid-stream.
        try { _idle.Wait(1000); } catch { /* ignore */ }
        try { _ffmpeg?.Dispose(); } catch { /* ignore */ }
        _ffmpeg = null;
        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
        try { _idle.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>One display's slice of the panel: which buffer, the source rectangle in the
    /// 480×270 frame, and the destination size (the display's own pixels).</summary>
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

        public static DisplayTarget Slice(string id, int srcX, int srcWidth, int destWidth, int destHeight) => new()
        {
            DisplayId = id,
            IsFullFrame = false,
            SrcRect = new SKRect(srcX, 0, srcX + srcWidth, PanelHeight),
            DestWidth = destWidth,
            DestHeight = destHeight
        };
    }
}
