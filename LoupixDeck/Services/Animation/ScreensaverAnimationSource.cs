using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using LoupixDeck.Registry;

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
    // Panel geometry / frame size come from the device (the continuous virtual panel the
    // wallpaper system assumes). ffmpeg is told to scale to exactly this size and its stdout
    // is read in fixed frame-sized chunks, so a stale panel size would not merely letterbox —
    // it would desync the whole stream.
    private readonly DeviceGeometry _geometry;

    // Read-ahead depth: how many realtime-paced frames may sit queued ahead of presentation.
    // The cushion (≈ FrameQueueDepth / fps seconds) lets the background reader ride out a
    // transient decode spike on a large/high-bitrate clip (e.g. a fat keyframe) without
    // starving the scheduler, and gives a lagging device a few frames to drop-skip back to
    // realtime. The bound keeps memory flat (FrameQueueDepth × one frame ≈ 3 MB at 6) and
    // caps how stale a dropped-to frame can be.
    private const int FrameQueueDepth = 6;

    private readonly LoupedeckDevice.Device.LoupedeckDevice _device;
    private readonly string _absoluteVideoPath;
    private readonly int _fps;
    private readonly bool _loop;
    private readonly Action _onEnded;

    // Shared full-display transfer path (per-display slicing + atomic blit + mid-write guard).
    private readonly FullDisplayFrameWriter _writer;

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

    public ScreensaverAnimationSource(LoupedeckDevice.Device.LoupedeckDevice device,
        string absoluteVideoPath, int fps, bool loop, Action onEnded)
    {
        _device = device;
        _geometry = device.Geometry;
        _absoluteVideoPath = absoluteVideoPath;
        _fps = Math.Clamp(fps <= 0 ? 30 : fps, 1, 120);
        _loop = loop;
        _onEnded = onEnded;
        _writer = new FullDisplayFrameWriter(device, _debug, "[Screensaver]");
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
        if (_writer.TargetCount == 0)
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
        // scale=…:flags=fast_bilinear — the panel is tiny, so the source is always
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
            $"-an -f rawvideo -r {_fps} -pix_fmt bgra -vf scale={_geometry.PanelWidth}:{_geometry.PanelHeight}:flags=fast_bilinear -";

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
                var buffer = ArrayPool<byte>.Shared.Rent(_geometry.FrameBytes);

                // Read exactly one full panel frame.
                var read = 0;
                var ended = false;
                while (read < _geometry.FrameBytes)
                {
                    int r;
                    try
                    {
                        r = await stream.ReadAsync(buffer.AsMemory(read, _geometry.FrameBytes - read), token)
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
                await _writer.PushAsync(buffer, token).ConfigureAwait(false);
                return;
            }

            // Split the per-frame cost into queue-wait vs device-push so we can tell decode/queue
            // latency apart from the serial framebuffer write.
            var readMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
            var pushStart = Stopwatch.GetTimestamp();
            await _writer.PushAsync(buffer, token).ConfigureAwait(false);
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
        // …then dispose the writer, which waits for any frame currently being drawn to the
        // device to finish, so the caller can safely close the serial port without cutting a
        // write mid-stream.
        try { _writer.Dispose(); } catch { /* ignore */ }
        try { _ffmpeg?.Dispose(); } catch { /* ignore */ }
        _ffmpeg = null;
        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
    }
}
