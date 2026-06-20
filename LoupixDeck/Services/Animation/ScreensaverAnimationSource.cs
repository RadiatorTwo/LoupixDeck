using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Full-display animated screensaver source (issue #120). Decodes the configured clip
/// (GIF or any ffmpeg-supported video) into a stream of raw BGRA frames via an external
/// <c>ffmpeg</c> process and fans each frame out across every display of the device.
///
/// It is driven by the central <see cref="IAnimationScheduler"/> — the scheduler ticks
/// <see cref="RenderFrameAsync"/> at the configured rate, which dequeues the next decoded
/// frame from a small bounded read-ahead queue. A background reader keeps that queue filled
/// from ffmpeg's stdout. ffmpeg is realtime-paced (<c>-re</c>), so the queue tracks wall-clock
/// time: the cushion (≤ <see cref="FrameQueueDepth"/> frames) absorbs per-frame decode jitter
/// on large clips, and when a device can't keep up (CPU / global Skia-gate contention with a
/// second device) the consumer drops to the freshest queued frame. That keeps playback at the
/// correct speed and skips frames instead of sliding into slow motion. Each device runs its
/// own ffmpeg + queue, so the two never share a clock.
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
    private const int FrameBytes = PanelWidth * PanelHeight * 4;

    // Read-ahead depth: how many realtime-paced frames may sit queued ahead of presentation.
    // The cushion (≈ FrameQueueDepth / fps seconds) lets the background reader ride out a
    // transient decode spike on a large/high-bitrate clip (e.g. a fat keyframe) without
    // starving the scheduler, and gives a lagging device a few frames to drop-skip back to
    // realtime. The bound keeps memory flat (FrameQueueDepth × FrameBytes ≈ 3 MB at 6) and
    // caps how stale a dropped-to frame can be.
    private const int FrameQueueDepth = 6;

    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;
    private readonly string _absoluteVideoPath;
    private readonly int _fps;
    private readonly bool _loop;
    private readonly Action _onEnded;

    private readonly List<DisplayTarget> _targets = [];

    private Process _ffmpeg;
    private Stream _stdout;
    private Channel<byte[]> _frames;
    private CancellationTokenSource _cts;
    private volatile bool _active;
    private int _endedSignalled;
    private long _startTimestamp;
    private bool _firstFrameLogged;

    // Diagnostics (issue #120): opt-in via env var LOUPIX_SS_DEBUG=1. When on, ffmpeg runs at
    // -loglevel verbose and its stderr is echoed, and every DebugReportEvery frames we print a
    // per-frame breakdown (pipe-read ms vs device-push ms) so the real bottleneck — ffmpeg, the
    // pipe, or the serial framebuffer write — is visible instead of guessed.
    private static readonly bool _debug =
        Environment.GetEnvironmentVariable("LOUPIX_SS_DEBUG") is "1" or "true" or "True";
    private const int DebugReportEvery = 30;
    private long _dbgFrames;
    private double _dbgReadMs;
    private double _dbgPushMs;
    private long _dbgDropped;
    private long _dbgWindowStart;

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
        // -probesize make it start decoding immediately.
        //
        // NOTE: do NOT add "-fflags nobuffer" here. On some clips it makes ffmpeg misread the
        // input start time (first frame reported at pts ~10 s) and pad the output with ~240
        // duplicated frames at the front (constant-rate "*** N dup!"), so the consumer reads
        // seconds of a single frozen frame and the screensaver looks blank/stuck on launch.
        // -analyzeduration 0 + -probesize already give immediate startup without that bug.
        //
        // -stream_loop -1 loops the input forever (what a screensaver wants); -r gives
        // constant-rate output so frame i == content-time i/fps.
        //
        // -re paces ffmpeg's OUTPUT to wall-clock (realtime) instead of letting the consumer's
        // read rate set the speed. This is what stops "slow motion" when the device can't keep
        // up (e.g. two devices contending for CPU and the global Skia conversion gate): ffmpeg
        // keeps emitting at the real frame rate, frames queue, and the consumer drops to the
        // freshest one (see RenderFrameAsync) so playback stays at the right speed and instead
        // skips frames. Without -re a slow consumer would just decode slower → slow motion.
        //
        // scale=…:flags=fast_bilinear — the panel is tiny (480×270), so the source is always
        // downscaled hard; fast_bilinear is the cheapest scaler and measured ~15% more decode
        // headroom than the default bicubic on 1080p clips with no visible quality loss at this
        // size. Hardware decode (-hwaccel) was tried and is slower here (the GPU→system-memory
        // download for the CPU scaler outweighs the decode saving), so it is intentionally off.
        var loopArg = _loop ? "-stream_loop -1 " : string.Empty;
        var logLevel = _debug ? "verbose" : "error";
        var args =
            $"-hide_banner -loglevel {logLevel} " +
            "-probesize 500000 -analyzeduration 0 -re " +
            $"{loopArg}-i \"{_absoluteVideoPath}\" " +
            $"-an -f rawvideo -r {_fps} -pix_fmt bgra -vf scale={PanelWidth}:{PanelHeight}:flags=fast_bilinear -";

        if (_debug)
            Console.WriteLine($"[Screensaver] ffmpeg {args}");

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
        // Bounded read-ahead queue: SingleReader (the scheduler) + SingleWriter (the producer
        // task below). FullMode.Wait gives ffmpeg backpressure once FrameQueueDepth frames are
        // buffered, so it can't outrun us and balloon memory.
        _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(FrameQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _startTimestamp = Stopwatch.GetTimestamp();
        _dbgWindowStart = _startTimestamp;

        // Drain stderr continuously: ffmpeg logs progress there and stalls if the pipe fills.
        // In debug mode we echo each line so ffmpeg's own diagnostics are visible; otherwise we
        // just drain and discard.
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                if (_debug)
                {
                    string line;
                    while ((line = await _ffmpeg.StandardError.ReadLineAsync(token)) != null)
                        Console.WriteLine($"[Screensaver][ffmpeg] {line}");
                }
                else
                {
                    _ = await _ffmpeg.StandardError.ReadToEndAsync(token);
                }
            }
            catch { /* killed / cancelled */ }
        }, token);

        // Background reader: keeps the read-ahead queue full from ffmpeg's stdout.
        _ = Task.Run(() => ProduceFramesAsync(token), token);

        _active = true;
        return true;
    }

    /// <summary>
    /// Background producer: reads full BGRA frames from ffmpeg's stdout into the bounded
    /// <see cref="_frames"/> queue. The bound applies backpressure, so ffmpeg decodes at most
    /// <see cref="FrameQueueDepth"/> frames ahead of presentation — enough cushion to ride out
    /// decode-time spikes on large clips without ever buffering unbounded. Completes the queue
    /// on EOF/error so the consumer can signal the clip ended. Frame buffers are pooled.
    /// </summary>
    private async Task ProduceFramesAsync(CancellationToken token)
    {
        var stream = _stdout;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(FrameBytes);

                // Read exactly one full panel frame.
                var read = 0;
                var ended = false;
                while (read < FrameBytes)
                {
                    int r;
                    try
                    {
                        r = await stream.ReadAsync(buffer.AsMemory(read, FrameBytes - read), token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        return; // stopped / disposed
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Screensaver] frame read failed: {ex.Message}");
                        ArrayPool<byte>.Shared.Return(buffer);
                        ended = true;
                        break;
                    }

                    if (r <= 0)
                    {
                        // End of stream: a non-looping clip finished, or ffmpeg exited.
                        ArrayPool<byte>.Shared.Return(buffer);
                        ended = true;
                        break;
                    }

                    read += r;
                }

                if (ended) break;

                if (!_firstFrameLogged)
                {
                    _firstFrameLogged = true;
                    var ms = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
                    Console.WriteLine($"[Screensaver] first frame after {ms:F0} ms.");
                }

                try
                {
                    // Blocks here while the queue is full — this is the backpressure that paces
                    // ffmpeg to our presentation rate.
                    await _frames.Writer.WriteAsync(buffer, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screensaver] frame producer error: {ex.Message}");
        }
        finally
        {
            _frames.Writer.TryComplete();
        }
    }

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_active) return;

        var channel = _frames;
        if (channel == null) return;

        var token = _cts?.Token ?? context.CancellationToken;

        var readStart = _debug ? Stopwatch.GetTimestamp() : 0;

        // Dequeue the next decoded frame from the read-ahead queue. Near-instant while the
        // producer keeps it full; only blocks if decode fell behind (graceful slow-down).
        byte[] buffer;
        try
        {
            buffer = await channel.Reader.ReadAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // stopped / disposed
        }
        catch (ChannelClosedException)
        {
            // Producer finished: a non-looping clip ended, or ffmpeg exited.
            SignalEnded();
            return;
        }

        // Drop to the freshest queued frame. ffmpeg is realtime-paced (-re), so if this device
        // fell behind (CPU / global Skia-gate contention from another device), several frames
        // have already queued. Presenting the newest keeps playback at wall-clock speed — the
        // clip skips frames instead of sliding into slow motion. Skipped buffers go back to the
        // pool. The fast device almost never finds extras here, so it is unaffected.
        var dropped = 0;
        while (channel.Reader.TryRead(out var newer))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newer;
            dropped++;
        }

        try
        {
            if (!_debug)
            {
                await PushFrameAsync(buffer, token).ConfigureAwait(false);
                return;
            }

            // Split the per-frame cost into queue-wait vs device-push so we can tell decode/queue
            // latency apart from the serial framebuffer write.
            var readMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
            var pushStart = Stopwatch.GetTimestamp();
            await PushFrameAsync(buffer, token).ConfigureAwait(false);
            var pushMs = Stopwatch.GetElapsedTime(pushStart).TotalMilliseconds;

            _dbgReadMs += readMs;
            _dbgPushMs += pushMs;
            _dbgDropped += dropped;
            if (++_dbgFrames >= DebugReportEvery)
            {
                var windowMs = Stopwatch.GetElapsedTime(_dbgWindowStart).TotalMilliseconds;
                var effFps = windowMs > 0 ? _dbgFrames * 1000.0 / windowMs : 0;
                Console.WriteLine(
                    $"[Screensaver][perf] {_dbgFrames} frames | queue wait avg {_dbgReadMs / _dbgFrames:F1} ms | " +
                    $"push avg {_dbgPushMs / _dbgFrames:F1} ms | dropped {_dbgDropped} | " +
                    $"effective {effFps:F1} fps (target {_fps})");
                _dbgFrames = 0;
                _dbgReadMs = 0;
                _dbgPushMs = 0;
                _dbgDropped = 0;
                _dbgWindowStart = Stopwatch.GetTimestamp();
            }
        }
        finally
        {
            // Return the pooled buffer the producer rented.
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
            // Copy exactly one frame: the buffer is pooled (ArrayPool) so it may be larger
            // than FrameBytes — never use bgra.Length here.
            Marshal.Copy(bgra, 0, frame.GetPixels(), FrameBytes);

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
                if (_debug)
                {
                    var ts = Stopwatch.GetTimestamp();
                    await _device.DrawScreen(draw.Id, draw.Bitmap, refresh: true).ConfigureAwait(false);
                    var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
                    // Flag slow draws (a multi-second value means the FRAMEBUFF/DRAW ACK is
                    // timing out, not just slow serial throughput).
                    if (ms > 500)
                        Console.WriteLine($"[Screensaver][perf] slow DrawScreen('{draw.Id}'): {ms:F0} ms");
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
        try { _frames?.Writer.TryComplete(); } catch { /* ignore */ }
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
