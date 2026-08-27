using System.Buffers;
using LoupixDeck.PluginSdk;

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

    private volatile bool _enabled;
    private volatile bool _paused;
    private bool _started;
    private long _lastPushed = -1;

    public PluginFullDisplayAnimationSource(
        LoupedeckDevice.Device.LoupedeckDevice device, IFullDisplayRenderer renderer)
    {
        _renderer = renderer;
        _writer = new FullDisplayFrameWriter(device);
        // The surface is the device's real panel (480 × its own panel height), not a fixed
        // 480×270. A plugin reads these dimensions off the surface it is handed in OnStart,
        // so one that respects them fills the whole screen on every device; one that assumes
        // 480×270 still renders exactly as it did before on the devices that are 270 tall.
        _surface = new FullDisplaySurface
        {
            Width = _writer.PanelWidth,
            Height = _writer.PanelHeight,
            Stride = _writer.PanelWidth * 4,
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

        var buffer = ArrayPool<byte>.Shared.Rent(_writer.FrameBytes);
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
            if (result.IsFinal)
                _enabled = false;
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
