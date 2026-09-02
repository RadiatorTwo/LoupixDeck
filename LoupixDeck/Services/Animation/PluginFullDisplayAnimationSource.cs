using System.Buffers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Bridges a plugin-provided <see cref="IFullDisplayRenderer"/> into the central animation scheduler
/// (issue #124). The direct analogue of <see cref="ScreensaverAnimationSource"/> minus decoding: the
/// plugin owns frame production, this owns pulling the freshest frame each tick, dirty-checking it,
/// and pushing it across the device's displays via the shared <see cref="FullDisplayFrameWriter"/>.
/// </summary>
public sealed class PluginFullDisplayAnimationSource : IAnimationSource, IDisposable
{
    private readonly IFullDisplayRenderer _renderer;
    private readonly FullDisplayFrameWriter _writer;
    private readonly FullDisplaySurface _surface;
    private readonly Action _onEnded;

    private readonly DeviceGeometry _geometry;
    private volatile bool _enabled;
    private volatile bool _paused;
    private bool _started;
    private bool _endedRaised;
    private long _lastPushed = -1;

    /// <param name="onEnded">Optional: invoked once, right after the final frame was pushed, when the
    /// renderer reports <see cref="FullDisplayFrameResult.IsFinal"/>. Callers that own the display
    /// through a plugin session leave this null — the stream then simply freezes on its last frame
    /// and the plugin releases the session itself. The screensaver passes a callback so a one-shot
    /// renderer ends the screensaver, exactly like a non-looping video clip.</param>
    public PluginFullDisplayAnimationSource(
        LoupedeckDevice.Device.LoupedeckDevice device, IFullDisplayRenderer renderer,
        Action onEnded = null)
    {
        _renderer = renderer;
        _geometry = device.Geometry;
        _onEnded = onEnded;
        _writer = new FullDisplayFrameWriter(device);
        // The plugin sees the device's real panel through IRenderCanvas' runtime
        // Width/Height, so a differently sized panel needs no SDK change.
        _surface = new FullDisplaySurface
        {
            Width = _geometry.PanelWidth,
            Height = _geometry.PanelHeight,
            Stride = _geometry.PanelWidth * 4,
            PixelFormat = FullDisplayPixelFormat.Bgra8888
        };
    }

    /// <summary>How many displays this source will push to. Zero means the device has no drawable
    /// display and the caller should abort the start.</summary>
    public int TargetCount => _writer.TargetCount;

    public int TargetFps => _renderer.TargetFps;

    // The scheduler polls this every tick: pausing (device inactive) or the plugin itself going
    // idle stops frames without unregistering the source.
    public bool IsActive => _enabled && !_paused && _renderer.IsActive;

    /// <summary>Calls the plugin's <see cref="IFullDisplayRenderer.OnStart"/> with the target surface
    /// and arms the source. Returns false (having called nothing that needs undoing) when there is no
    /// drawable display or the plugin's OnStart throws.</summary>
    public bool Start()
    {
        if (_writer.TargetCount == 0)
            return false;

        try
        {
            _renderer.OnStart(_surface);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginFullDisplay] OnStart threw: {ex.Message}");
            return false;
        }

        _started = true;
        _enabled = true;
        return true;
    }

    /// <summary>Pauses/resumes frame pulls without releasing the session (used when the owning device
    /// goes inactive). The plugin keeps its decoder warm; the scheduler just stops ticking.</summary>
    public void SetPaused(bool paused) => _paused = paused;

    public async Task RenderFrameAsync(AnimationRenderContext context)
    {
        if (!_enabled || _paused)
            return;

        var buffer = ArrayPool<byte>.Shared.Rent(_geometry.FrameBytes);
        try
        {
            FullDisplayFrameContext frame = new()
            {
                FrameNumber = context.FrameNumber,
                Elapsed = context.Elapsed,
                Delta = context.Delta,
                EffectiveFps = context.EffectiveFps,
                Surface = _surface,
                CancellationToken = context.CancellationToken
            };

            FullDisplayFrameResult result;
            try
            {
                result = _renderer.RenderFrame(buffer, in frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginFullDisplay] RenderFrame threw: {ex.Message}");
                return;
            }

            // Dirty-check on the plugin's frame number: only blit when a genuinely new frame was
            // written, so idle ticks cost no serial I/O.
            if (result.Drawn && result.FrameNumber != _lastPushed)
            {
                await _writer.PushAsync(buffer, context.CancellationToken).ConfigureAwait(false);
                _lastPushed = result.FrameNumber;
            }

            // A one-shot source freezes on its last frame; the session stays open until released.
            // When a caller supplied an end callback it decides what "the stream ended" means for
            // it (the screensaver stops and repaints the page). Raised at most once.
            if (result.IsFinal)
            {
                _enabled = false;

                if (_onEnded != null && !_endedRaised)
                {
                    _endedRaised = true;
                    try { _onEnded(); }
                    catch (Exception ex) { Console.WriteLine($"[PluginFullDisplay] onEnded threw: {ex.Message}"); }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _enabled = false;

        // Only notify the plugin if it was actually started (a failed Start never ran OnStart).
        if (_started)
        {
            try { _renderer.OnStop(); }
            catch (Exception ex) { Console.WriteLine($"[PluginFullDisplay] OnStop threw: {ex.Message}"); }
        }

        // Waits for any in-flight frame push to finish so a serial write is never cut mid-stream.
        try { _writer.Dispose(); } catch { /* ignore */ }
    }
}
