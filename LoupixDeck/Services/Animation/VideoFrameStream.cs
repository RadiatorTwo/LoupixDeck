using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// One decoded frame handed out by <see cref="VideoFrameStream.ReadFreshestAsync"/>. It MUST be
/// handed back via <see cref="VideoFrameStream.Release"/> once presented. In streaming mode
/// <see cref="Buffer"/> is rented from <see cref="ArrayPool{T}"/> and may be larger than one
/// frame, so consumers read exactly <see cref="VideoFrameStream.FrameBytes"/> from it, never
/// <c>Buffer.Length</c>; in ring mode it is a frame the ring owns for the stream's lifetime and
/// releasing it is a no-op.
/// </summary>
public readonly struct VideoFrame
{
    public VideoFrame(byte[] buffer, int dropped, bool ended, bool pooled = false)
    {
        Buffer = buffer;
        Dropped = dropped;
        Ended = ended;
        Pooled = pooled;
    }

    /// <summary>The frame's raw BGRA pixels, or null when no frame was produced.</summary>
    public byte[] Buffer { get; }

    /// <summary>How many staler queued frames were skipped to reach this one (see the
    /// drop-to-freshest rationale in <see cref="VideoFrameStream.ReadFreshestAsync"/>).</summary>
    public int Dropped { get; }

    /// <summary>True when the producer finished: a non-looping clip ended, or ffmpeg exited.</summary>
    public bool Ended { get; }

    /// <summary>Whether <see cref="Buffer"/> came from the shared array pool (streaming mode) and
    /// must go back to it, rather than being owned by the ring.</summary>
    public bool Pooled { get; }

    public bool HasFrame => Buffer != null;
}

/// <summary>
/// Decodes a clip (GIF or any ffmpeg-supported video) into a stream of raw BGRA frames of a fixed
/// size, via an external <c>ffmpeg</c> process. Factored out of
/// <see cref="ScreensaverAnimationSource"/> so every animated full-display consumer shares one
/// battle-tested decode path instead of copying the argument layout and the queue discipline.
///
/// There are two loop strategies, chosen once at <see cref="Start"/>:
///
/// <list type="bullet">
///   <item><b>Streaming</b> — ffmpeg decodes continuously, realtime-paced (<c>-re</c>), so the
///     bounded read-ahead queue tracks wall-clock time: the cushion
///     (≤ <see cref="FrameQueueDepth"/> frames) absorbs per-frame decode jitter on large clips,
///     and when a consumer can't keep up (CPU / global Skia-gate contention with a second device)
///     <see cref="ReadFreshestAsync"/> drops to the freshest queued frame. That keeps playback at
///     the correct speed and skips frames instead of sliding into slow motion. Looping uses
///     <c>-stream_loop -1</c>, which is NOT seamless — see the ring mode note below.</item>
///   <item><b>Ring</b> — a short looping clip is decoded once, as fast as ffmpeg can (no
///     <c>-re</c>), into a frame ring held in RAM; playback then replays from memory and ffmpeg
///     exits. This exists because <c>-stream_loop</c> loops at the DEMUXER: every pass hits EOF and
///     the decoder is flushed and re-initialised, measurably costing ~0.5 s (14 dropped frames, a
///     dip from 30 to ~20 fps) at each seam. Unnoticeable once at the end of a film; unacceptable
///     every few seconds on an animated background, which is exactly what the ring serves.</item>
/// </list>
///
/// Each consumer runs its own stream, so two never share a clock.
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

    // Ring-mode budget. The ring holds every frame of the clip uncompressed, so it is only viable
    // for short loops: at a 480×270 panel one frame is ~518 KB, making 96 MB ≈ 185 frames ≈ 6 s at
    // 30 fps or 12 s at 15 fps. The seconds cap is the second guard, so a long clip whose frames
    // happen to be small (a tiny panel) still streams rather than eating RAM. Both are per stream,
    // and every device runs its own — keep that in mind before raising them.
    private const long RingByteBudget = 96L * 1024 * 1024;
    private const double RingMaxSeconds = 15.0;

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

    // Ring mode (see the class summary). _ring is allocated once at its final length and filled
    // strictly front-to-back by the fill task; _ringCount is published with a release write after
    // each frame is stored, so a consumer that reads it never sees a slot it may not read.
    private bool _ringMode;
    private byte[][] _ring;
    private int _ringCount;
    private volatile bool _ringComplete;
    private long _playbackStart;
    private int _lastRingIndex = -1;

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

        // A seamless loop is only worth buying for a clip that actually fits in RAM; everything
        // else streams. A non-looping clip has no seam to fix, so it streams too.
        var ringFrames = 0;
        _ringMode = _loop && TryPlanRing(out ringFrames);
        if (_ringMode) _ring = new byte[ringFrames][];

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
        // Ring mode decodes the clip exactly once and then replays from RAM, so it neither loops
        // the input nor wants realtime pacing — -re would stretch the prefill over the clip's own
        // running time before a single frame could be shown.
        var loopArg = (_loop && !_ringMode) ? "-stream_loop -1 " : string.Empty;
        var paceArg = _ringMode ? string.Empty : "-re ";
        var logLevel = _debug ? "verbose" : "error";
        var args =
            $"-hide_banner -loglevel {logLevel} " +
            $"-probesize 500000 -analyzeduration 0 {paceArg}" +
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
        if (!_ringMode)
        {
            // Bounded read-ahead queue: SingleReader (the consumer) + SingleWriter (the producer
            // task below). FullMode.Wait gives ffmpeg backpressure once FrameQueueDepth frames are
            // buffered, so it can't outrun us and balloon memory.
            _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(FrameQueueDepth)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
        }

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

        // Background reader: fills the ring once, or keeps the read-ahead queue full from
        // ffmpeg's stdout for the lifetime of the stream.
        _ = _ringMode
            ? Task.Run(() => FillRingAsync(token), token)
            : Task.Run(() => ProduceFramesAsync(token), token);

        return true;
    }

    /// <summary>
    /// Decides whether this clip can loop from RAM. Needs a known duration (via <c>ffprobe</c>)
    /// that fits both <see cref="RingMaxSeconds"/> and <see cref="RingByteBudget"/>. Anything
    /// unknown or too large falls back to streaming, so a failure here is never fatal — it only
    /// costs the seamless seam.
    /// </summary>
    private bool TryPlanRing(out int frameCount)
    {
        frameCount = 0;

        var seconds = ProbeDurationSeconds();
        if (seconds is not > 0) return false;
        if (seconds > RingMaxSeconds)
        {
            if (_debug)
                Console.WriteLine(
                    $"{_logPrefix} streaming loop: {seconds:F1} s exceeds the {RingMaxSeconds:F0} s ring cap.");
            return false;
        }

        // One frame of slack: ffmpeg's constant-rate output can emit a final partial-interval
        // frame, and a ring one short would clip the loop.
        var frames = (int)Math.Ceiling(seconds.Value * _fps) + 1;
        if (frames < 1) return false;

        var bytes = (long)frames * FrameBytes;
        if (bytes > RingByteBudget)
        {
            if (_debug)
                Console.WriteLine(
                    $"{_logPrefix} streaming loop: {frames} frames would need {bytes / (1024.0 * 1024.0):F0} MB.");
            return false;
        }

        frameCount = frames;
        Console.WriteLine(
            $"{_logPrefix} ring loop: up to {frames} frames ({seconds:F1} s, {bytes / (1024.0 * 1024.0):F0} MB).");
        return true;
    }

    /// <summary>
    /// Reads the clip's duration in seconds via <c>ffprobe</c>, or null when it can't be
    /// determined (ffprobe missing, a stream without a duration, unparsable output).
    /// </summary>
    private double? ProbeDurationSeconds()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments =
                    $"-v error -show_entries format=duration -of default=nw=1:nk=1 \"{_absoluteVideoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (probe == null) return null;

            var output = probe.StandardOutput.ReadToEnd();
            if (!probe.WaitForExit(3000))
            {
                try { probe.Kill(true); } catch { /* best effort */ }
                return null;
            }

            if (probe.ExitCode != 0) return null;

            // ffprobe prints an invariant decimal point, and "N/A" for a duration-less input.
            return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : null;
        }
        catch
        {
            // ffprobe not on PATH or failed to launch — stream instead.
            return null;
        }
    }

    /// <summary>
    /// Fills the frame ring once from ffmpeg's stdout, then lets ffmpeg exit. Unlike the streaming
    /// producer these buffers are owned for the stream's lifetime rather than pooled, because the
    /// consumer replays them indefinitely. Stops at EOF or when the planned length is reached, so
    /// an input whose probed duration understated its real length can't overrun the ring.
    /// </summary>
    private async Task FillRingAsync(CancellationToken token)
    {
        var stream = _stdout;
        var frameBytes = FrameBytes;
        var ring = _ring;
        try
        {
            while (!token.IsCancellationRequested && _ringCount < ring.Length)
            {
                var buffer = new byte[frameBytes];

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
                        return; // stopped / disposed
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{_logPrefix} frame read failed: {ex.Message}");
                        ended = true;
                        break;
                    }

                    if (r <= 0)
                    {
                        // Clip fully decoded (the expected exit) or ffmpeg died early.
                        ended = true;
                        break;
                    }

                    read += r;
                }

                // A short final read means a truncated frame — drop it rather than show garbage.
                if (ended) break;

                if (!_firstFrameLogged)
                {
                    _firstFrameLogged = true;
                    var firstMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
                    Console.WriteLine($"{_logPrefix} first frame after {firstMs:F0} ms.");
                }

                ring[_ringCount] = buffer;
                // Release write: the frame is fully stored before the consumer can see the slot.
                Volatile.Write(ref _ringCount, _ringCount + 1);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{_logPrefix} frame producer error: {ex.Message}");
        }
        finally
        {
            _ringComplete = true;
            var ms = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
            Console.WriteLine($"{_logPrefix} ring filled: {_ringCount} frames in {ms:F0} ms.");

            // ffmpeg has nothing left to do; the ring is the source from here on.
            try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { /* already gone */ }
        }
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
        if (_ringMode) return ReadFromRing();

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

        return new VideoFrame(buffer, dropped, ended: false, pooled: true);
    }

    /// <summary>
    /// Picks the ring frame for the elapsed playback time rather than simply advancing one slot,
    /// so content timing follows wall-clock exactly as ffmpeg's <c>-re</c> pacing does in
    /// streaming mode: a consumer that falls behind skips frames instead of sliding into slow
    /// motion. Never blocks — while the ring is still filling it holds the newest frame, and it
    /// returns no frame at all until the first one has been decoded.
    /// </summary>
    private VideoFrame ReadFromRing()
    {
        var count = Volatile.Read(ref _ringCount);
        if (count <= 0) return default; // still decoding the first frame

        if (_playbackStart == 0) _playbackStart = Stopwatch.GetTimestamp();

        var elapsedMs = Stopwatch.GetElapsedTime(_playbackStart).TotalMilliseconds;
        var position = (long)(elapsedMs * _fps / 1000.0);

        // While filling, hold on the newest decoded frame rather than looping a partial clip.
        var index = _ringComplete ? (int)(position % count) : (int)Math.Min(position, count - 1);

        // Frames skipped since the last presented one. Negative across the loop seam, which is
        // not a drop — it is the loop.
        var dropped = _lastRingIndex >= 0 ? Math.Max(0, index - _lastRingIndex - 1) : 0;
        _lastRingIndex = index;

        return new VideoFrame(_ring[index], dropped, ended: false);
    }

    /// <summary>
    /// Hands a presented frame back. A streaming frame goes to the pool the producer rented it
    /// from; a ring frame is owned by the ring and outlives the presentation, so it is kept.
    /// </summary>
    public void Release(in VideoFrame frame)
    {
        if (frame.Pooled && frame.Buffer != null) ArrayPool<byte>.Shared.Return(frame.Buffer);
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
        // Drop the ring so its frames (tens of MB) are collectable. Plain managed arrays, and the
        // consumer is stopped by the time anything disposes us, so there is nothing to unpin.
        _ring = null;
    }
}
