using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// One decoded frame handed out by <see cref="VideoFrameStream.ReadFreshestAsync"/>.
/// <see cref="Buffer"/> is rented from <see cref="ArrayPool{T}"/> and MUST be handed back via
/// <see cref="VideoFrameStream.Release"/> once presented — it may be larger than one frame, so
/// consumers read exactly <see cref="VideoFrameStream.FrameBytes"/> from it, never
/// <c>Buffer.Length</c>.
/// </summary>
public readonly struct VideoFrame
{
    public VideoFrame(byte[] buffer, int dropped, bool ended)
    {
        Buffer = buffer;
        Dropped = dropped;
        Ended = ended;
    }

    /// <summary>The frame's raw BGRA pixels, or null when no frame was produced.</summary>
    public byte[] Buffer { get; }

    /// <summary>How many staler queued frames were skipped to reach this one (see the
    /// drop-to-freshest rationale in <see cref="VideoFrameStream.ReadFreshestAsync"/>).</summary>
    public int Dropped { get; }

    /// <summary>True when the producer finished: a non-looping clip ended, or ffmpeg exited.</summary>
    public bool Ended { get; }

    public bool HasFrame => Buffer != null;
}

/// <summary>
/// Decodes a clip (GIF or any ffmpeg-supported video) into a stream of raw BGRA frames of a fixed
/// size, via an external <c>ffmpeg</c> process. Factored out of
/// <see cref="ScreensaverAnimationSource"/> so every animated full-display consumer shares one
/// battle-tested decode path instead of copying the argument layout and the queue discipline.
///
/// ffmpeg is realtime-paced (<c>-re</c>), so the bounded read-ahead queue tracks wall-clock time:
/// the cushion (≤ <see cref="FrameQueueDepth"/> frames) absorbs per-frame decode jitter on large
/// clips, and when a consumer can't keep up (CPU / global Skia-gate contention with a second
/// device) <see cref="ReadFreshestAsync"/> drops to the freshest queued frame. That keeps playback
/// at the correct speed and skips frames instead of sliding into slow motion. Each consumer runs
/// its own stream, so two never share a clock.
/// </summary>
public sealed class VideoFrameStream : IDisposable
{
    // Read-ahead depth: how many realtime-paced frames may sit queued ahead of presentation.
    // The cushion (≈ FrameQueueDepth / fps seconds) lets the background reader ride out a
    // transient decode spike on a large/high-bitrate clip (e.g. a fat keyframe) without
    // starving the consumer, and gives a lagging consumer a few frames to drop-skip back to
    // realtime. The bound keeps memory flat (FrameQueueDepth × one frame ≈ 3 MB at 6) and
    // caps how stale a dropped-to frame can be.
    private const int FrameQueueDepth = 6;

    private readonly string _absoluteVideoPath;
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private readonly int _fps;
    private readonly bool _loop;
    private readonly bool _debug;
    private readonly string _logPrefix;

    private Process _ffmpeg;
    private Stream _stdout;
    private Channel<byte[]> _frames;
    private CancellationTokenSource _cts;
    private long _startTimestamp;
    private bool _firstFrameLogged;

    public VideoFrameStream(
        string absoluteVideoPath,
        int frameWidth,
        int frameHeight,
        int fps,
        bool loop,
        bool debug = false,
        string logPrefix = "[Video]")
    {
        _absoluteVideoPath = absoluteVideoPath;
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _fps = fps;
        _loop = loop;
        _debug = debug;
        _logPrefix = logPrefix;
    }

    /// <summary>Size of exactly one decoded frame. Frame buffers may be larger (pooled).</summary>
    public int FrameBytes => _frameWidth * _frameHeight * 4;

    /// <summary>
    /// The stream's own cancellation token, or null before <see cref="Start"/> / after
    /// <see cref="Dispose"/>. Consumers use it for the work they do with a frame so a stop
    /// aborts them too.
    /// </summary>
    public CancellationToken? Token => _cts?.Token;

    /// <summary>
    /// Spawns ffmpeg and starts the background reader. Returns false (having started nothing)
    /// when the process can't be launched, so the caller can abort cleanly. Synchronous: only
    /// spawns the process.
    /// </summary>
    public bool Start()
    {
        _cts = new CancellationTokenSource();

        // Argument layout matters: global opts, then INPUT opts (before -i), then OUTPUT opts.
        //
        // Startup latency fix: by default ffmpeg analyses up to ~5 s of the input
        // (-analyzeduration) before emitting the first frame, so playback appeared to "hang"
        // for seconds after the start. -analyzeduration 0 + a small -probesize make it start
        // decoding immediately.
        //
        // NOTE: do NOT add "-fflags nobuffer" here. On some clips it makes ffmpeg misread the
        // input start time (first frame reported at pts ~10 s) and pad the output with ~240
        // duplicated frames at the front (constant-rate "*** N dup!"), so the consumer reads
        // seconds of a single frozen frame and the display looks blank/stuck on launch.
        // -analyzeduration 0 + -probesize already give immediate startup without that bug.
        //
        // -stream_loop -1 loops the input forever; -r gives constant-rate output so frame i ==
        // content-time i/fps.
        //
        // -re paces ffmpeg's OUTPUT to wall-clock (realtime) instead of letting the consumer's
        // read rate set the speed. This is what stops "slow motion" when the device can't keep
        // up (e.g. two devices contending for CPU and the global Skia conversion gate): ffmpeg
        // keeps emitting at the real frame rate, frames queue, and the consumer drops to the
        // freshest one (see ReadFreshestAsync) so playback stays at the right speed and instead
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
            $"-an -f rawvideo -r {_fps} -pix_fmt bgra -vf scale={_frameWidth}:{_frameHeight}:flags=fast_bilinear -";

        if (_debug)
            Console.WriteLine($"{_logPrefix} ffmpeg {args}");

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
            Console.WriteLine($"{_logPrefix} ffmpeg start failed: {ex.Message}");
            return false;
        }

        if (_ffmpeg == null)
        {
            Console.WriteLine($"{_logPrefix} ffmpeg failed to start (is it on PATH?).");
            return false;
        }

        _stdout = _ffmpeg.StandardOutput.BaseStream;
        // Bounded read-ahead queue: SingleReader (the consumer) + SingleWriter (the producer
        // task below). FullMode.Wait gives ffmpeg backpressure once FrameQueueDepth frames are
        // buffered, so it can't outrun us and balloon memory.
        _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(FrameQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _startTimestamp = Stopwatch.GetTimestamp();

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
                        Console.WriteLine($"{_logPrefix}[ffmpeg] {line}");
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
        var frameBytes = FrameBytes;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

                // Read exactly one full panel frame.
                var read = 0;
                var ended = false;
                while (read < frameBytes)
                {
                    int r;
                    try
                    {
                        r = await stream.ReadAsync(buffer.AsMemory(read, frameBytes - read), token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        return; // stopped / disposed
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{_logPrefix} frame read failed: {ex.Message}");
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
                    Console.WriteLine($"{_logPrefix} first frame after {ms:F0} ms.");
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
            Console.WriteLine($"{_logPrefix} frame producer error: {ex.Message}");
        }
        finally
        {
            _frames.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Dequeues the next decoded frame, then advances to the freshest one already queued.
    /// Near-instant while the producer keeps the queue full; only blocks if decode fell behind
    /// (graceful slow-down).
    ///
    /// The drop-to-freshest step matters: ffmpeg is realtime-paced (-re), so if the consumer
    /// fell behind (CPU / global Skia-gate contention from another device), several frames have
    /// already queued. Presenting the newest keeps playback at wall-clock speed — the clip skips
    /// frames instead of sliding into slow motion. Skipped buffers go straight back to the pool.
    /// A fast consumer almost never finds extras here, so it is unaffected.
    ///
    /// Returns a frame with <see cref="VideoFrame.Ended"/> set once the producer completed, and
    /// a frameless default on cancellation or before <see cref="Start"/>.
    /// </summary>
    public async Task<VideoFrame> ReadFreshestAsync(CancellationToken token)
    {
        var channel = _frames;
        if (channel == null) return default;

        byte[] buffer;
        try
        {
            buffer = await channel.Reader.ReadAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return default; // stopped / disposed
        }
        catch (ChannelClosedException)
        {
            // Producer finished: a non-looping clip ended, or ffmpeg exited.
            return new VideoFrame(null, 0, ended: true);
        }

        var dropped = 0;
        while (channel.Reader.TryRead(out var newer))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newer;
            dropped++;
        }

        return new VideoFrame(buffer, dropped, ended: false);
    }

    /// <summary>Hands a presented frame's buffer back to the pool the producer rented it from.</summary>
    public void Release(byte[] buffer)
    {
        if (buffer != null) ArrayPool<byte>.Shared.Return(buffer);
    }

    /// <summary>
    /// Cancels the read (aborting a blocked ReadAsync) and stops ffmpeg. Callers that also own a
    /// device transfer path must dispose that afterwards, so an in-flight frame write finishes
    /// before the serial port closes.
    /// </summary>
    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* already disposed */ }
        try { _frames?.Writer.TryComplete(); } catch { /* ignore */ }
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { /* already gone */ }
        try { _ffmpeg?.Dispose(); } catch { /* ignore */ }
        _ffmpeg = null;
        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
    }
}
