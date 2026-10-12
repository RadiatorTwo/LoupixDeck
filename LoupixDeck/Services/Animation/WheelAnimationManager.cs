using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.Screensaver;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="IWheelAnimationManager"/>
public sealed class WheelAnimationManager : IWheelAnimationManager, IDisposable
{
    // Tick rate of an animated wheel image. Deliberately below the grid and strips: every push is a
    // full 240x240 frame on the serial link the touch screen shares, so a higher rate would buy
    // little and cost the main screen its bandwidth.
    private const int ImageFps = 10;

    private readonly IPageManager _pageManager;
    private readonly IAnimationScheduler _scheduler;
    private readonly IAnimatedImageCache _cache;
    private readonly IScreensaverManager _screensaver;
    private readonly IServiceProvider _deviceProvider;

    private readonly WheelAnimationSource _source;

    private readonly Lock _gate = new();
    private bool _started;
    private bool _disposed;
    private volatile bool _screensaverActive;
    private IDeviceController _controller;

    // The wheel canvas we currently listen to for live edits. Re-derived on every Rescan so a
    // freshly imported animation starts immediately, not only on the next navigation.
    private TouchButton _subscribedCanvas;

    public WheelAnimationManager(
        IPageManager pageManager,
        IAnimationScheduler scheduler,
        IAnimatedImageCache cache,
        IScreensaverManager screensaver,
        IServiceProvider deviceProvider)
    {
        _pageManager = pageManager;
        _scheduler = scheduler;
        _cache = cache;
        _screensaver = screensaver;
        _deviceProvider = deviceProvider;

        _source = new WheelAnimationSource(deviceProvider);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _pageManager.TouchLayoutChanged += OnLayoutChanged;
        _screensaver.Started += OnScreensaverStarted;
        _screensaver.Stopped += OnScreensaverStopped;

        _controller = _deviceProvider.GetService<IDeviceController>();
        if (_controller != null)
            _controller.WheelModeChanged += OnLayoutChanged;

        _scheduler.Register(_source);
        Rescan();
    }

    private void OnLayoutChanged() => Rescan();

    public void Rescan()
    {
        if (_disposed) return;

        // Read the canvas on the UI thread (layer collections are editor-bound), then hand the
        // decode off to a background task: same shape as SideDisplayAnimationManager.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            List<Spec> specs = BuildSpecs();

            _ = Task.Run(() =>
            {
                try
                {
                    _source.SetEntries(Materialize(specs));
                    UpdateEnabled();
                    if (_source.IsActive)
                        _scheduler.RequestFrame(_source);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WheelAnimationManager: rescan failed: {ex.Message}");
                }
            });
        });
    }

    /// <summary>UI-thread pass: reads the active wheel mode's canvas and (re)subscribes to its change
    /// events so a live edit restarts the animation immediately.</summary>
    private List<Spec> BuildSpecs()
    {
        List<Spec> specs = [];

        // Only a device with a wheel screen has a canvas to animate.
        TouchButton canvas = _controller is { WheelScreenSize: > 0 }
            ? _pageManager.CurrentTouchButtonPage?.Wheel?.Canvas
            : null;

        SubscribeCanvas(canvas);

        if (canvas?.Layers == null) return specs;

        foreach (LayerBase layer in canvas.Layers)
        {
            if (layer is ImageLayer { IsAnimated: true } img &&
                !string.IsNullOrWhiteSpace(img.AnimatedAssetPath))
            {
                specs.Add(new Spec { ImageLayer = img, AnimPath = img.AnimatedAssetPath });
            }
        }

        return specs;
    }

    /// <summary>Background pass: decodes animated images (cached) and builds the source entries.</summary>
    private List<WheelAnimationSource.WheelEntry> Materialize(List<Spec> specs)
    {
        List<WheelAnimationSource.WheelEntry> entries = new(specs.Count);

        foreach (Spec spec in specs)
        {
            DecodedAnimation anim = _cache.Get(spec.AnimPath);
            if (anim == null) continue;

            // Seed the first frame so a wheel redraw shows content immediately, before the source's
            // first tick (no blank flash). Field swap, no notify.
            if (spec.ImageLayer.CachedImage == null && anim.Frames.Length > 0)
                spec.ImageLayer.SetAnimationFrame(anim.Frames[0]);

            entries.Add(new WheelAnimationSource.WheelEntry
            {
                Layer = spec.ImageLayer,
                Anim = anim,
                DesiredFps = ImageFps
            });
        }

        return entries;
    }

    private void SubscribeCanvas(TouchButton canvas)
    {
        if (ReferenceEquals(_subscribedCanvas, canvas)) return;

        if (_subscribedCanvas != null) _subscribedCanvas.ItemChanged -= OnCanvasItemChanged;
        _subscribedCanvas = canvas;
        if (canvas != null) canvas.ItemChanged += OnCanvasItemChanged;
    }

    // A layer edit (add/remove/retarget) rebuilds the entry set; the frame swap itself does not
    // raise ItemChanged (SetAnimationFrame is a silent field swap), so this never self-triggers.
    private void OnCanvasItemChanged(object sender, EventArgs e) => Rescan();

    private void OnScreensaverStarted()
    {
        _screensaverActive = true;
        _source.SetEnabled(false);
    }

    private void OnScreensaverStopped()
    {
        _screensaverActive = false;
        UpdateEnabled();
        if (_source.IsActive)
            _scheduler.RequestFrame(_source);
    }

    /// <summary>Enabled unless the screensaver owns the display. The device-off, display-takeover and
    /// open-menu states are checked by the controller on every push.</summary>
    private void UpdateEnabled() => _source.SetEnabled(!_screensaverActive);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _pageManager.TouchLayoutChanged -= OnLayoutChanged;
        _screensaver.Started -= OnScreensaverStarted;
        _screensaver.Stopped -= OnScreensaverStopped;
        if (_controller != null)
            _controller.WheelModeChanged -= OnLayoutChanged;

        SubscribeCanvas(null);

        try { _scheduler.Unregister(_source); } catch { /* best effort */ }
        try { _source.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>Intermediate captured on the UI thread before the background decode pass.</summary>
    private sealed class Spec
    {
        public ImageLayer ImageLayer { get; init; }
        public string AnimPath { get; init; }
    }
}
