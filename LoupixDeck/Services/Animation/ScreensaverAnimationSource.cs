using System.Diagnostics;
using LoupixDeck.Registry;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Full-display animated screensaver source (issue #120). Plays the configured clip
/// (GIF or any ffmpeg-supported video) by pulling raw BGRA frames from a
/// <see cref="VideoFrameStream"/> and fanning each one out across every display of the device.
///
/// It is driven by the central <see cref="IAnimationScheduler"/> — the scheduler ticks
/// <see cref="RenderFrameAsync"/> at the configured rate, which takes the freshest decoded frame
/// from the stream and pushes it. The decode discipline itself (ffmpeg's realtime pacing, the
/// bounded queue, drop-to-freshest when a device can't keep up, and the seamless RAM ring a short
/// looping clip gets instead) lives in <see cref="VideoFrameStream"/>; this source only presents.
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

    private readonly int _fps;
    private readonly Action _onEnded;

    // Shared decode path (ffmpeg process + read-ahead queue + drop-to-freshest).
    private readonly VideoFrameStream _stream;

    // Shared full-display transfer path (per-display slicing + atomic blit + mid-write guard).
    private readonly FullDisplayFrameWriter _writer;

    private volatile bool _active;
    private int _endedSignalled;

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
        _geometry = device.Geometry;
        _fps = Math.Clamp(fps <= 0 ? 30 : fps, 1, 120);
        _onEnded = onEnded;
        _stream = new VideoFrameStream(absoluteVideoPath, _geometry.PanelWidth, _geometry.PanelHeight,
            _fps, loop, _debug, "[Screensaver]");
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

        if (!_stream.Start()) return false;

        _dbgWindowStart = Stopwatch.GetTimestamp();
        _active = true;
        return true;
    }

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_active) return;

        var token = _stream.Token ?? context.CancellationToken;

        var readStart = _debug ? Stopwatch.GetTimestamp() : 0;

        var frame = await _stream.ReadFreshestAsync(token).ConfigureAwait(false);
        if (frame.Ended)
        {
            SignalEnded();
            return;
        }

        if (!frame.HasFrame) return; // stopped / disposed

        try
        {
            if (!_debug)
            {
                await _writer.PushAsync(frame.Buffer, token).ConfigureAwait(false);
                return;
            }

            // Split the per-frame cost into queue-wait vs device-push so we can tell decode/queue
            // latency apart from the serial framebuffer write.
            var readMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;
            var pushStart = Stopwatch.GetTimestamp();
            await _writer.PushAsync(frame.Buffer, token).ConfigureAwait(false);
            var pushMs = Stopwatch.GetElapsedTime(pushStart).TotalMilliseconds;

            _dbgReadMs += readMs;
            _dbgPushMs += pushMs;
            _dbgDropped += frame.Dropped;
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
            // Hand the frame back: pooled buffers return to the pool, ring frames are kept.
            _stream.Release(frame);
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
        try { _stream.Dispose(); } catch { /* ignore */ }
        // …then dispose the writer, which waits for any frame currently being drawn to the
        // device to finish, so the caller can safely close the serial port without cutting a
        // write mid-stream.
        try { _writer.Dispose(); } catch { /* ignore */ }
    }
}
