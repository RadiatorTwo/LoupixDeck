using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Screensaver;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="ISideDisplayAnimationManager"/>
public sealed class SideDisplayAnimationManager : ISideDisplayAnimationManager, IDisposable
{
    // Tick rate contributed by an animated strip image. Capped low for the same reason as buttons:
    // re-rendering a strip takes the global Skia gate and the source dirty-checks frames anyway.
    private const int ImageFps = 15;

    private readonly IPageManager _pageManager;
    private readonly IAnimationScheduler _scheduler;
    private readonly IAnimatedImageCache _cache;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFolderNavigationService _folderNav;
    private readonly IScreensaverManager _screensaver;

    private readonly SideDisplayAnimationSource _source;

    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;
    private volatile bool _screensaverActive;

    // Strip canvases we currently listen to for live edits (index 0 = Left, 1 = Right). Re-derived on
    // every Rescan so a freshly-imported animation starts immediately, not only on next navigation.
    private readonly TouchButton[] _subscribedCanvases = new TouchButton[2];

    public SideDisplayAnimationManager(
        IPageManager pageManager,
        IAnimationScheduler scheduler,
        IAnimatedImageCache cache,
        IExclusiveModeService exclusiveMode,
        IFolderNavigationService folderNav,
        IScreensaverManager screensaver,
        IServiceProvider deviceProvider)
    {
        _pageManager = pageManager;
        _scheduler = scheduler;
        _cache = cache;
        _exclusiveMode = exclusiveMode;
        _folderNav = folderNav;
        _screensaver = screensaver;

        _source = new SideDisplayAnimationSource(deviceProvider);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _pageManager.OnRotaryPageChanged += OnRotaryPageChanged;
        _screensaver.Started += OnScreensaverStarted;
        _screensaver.Stopped += OnScreensaverStopped;
        _exclusiveMode.StateChanged += OnTakeoverStateChanged;
        _folderNav.StateChanged += OnTakeoverStateChanged;

        _scheduler.Register(_source);
        Rescan();
    }

    // Both sides flow through one handler; a change on either column rebuilds the whole set.
    private void OnRotaryPageChanged(RotarySide side, int previous, int current) => Rescan();

    public void Rescan()
    {
        if (_disposed) return;

        // Enumerate the current strip canvases on the UI thread (layer collections are editor-bound),
        // then hand the heavy decode off to a background task — same shape as ButtonAnimationManager.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            var specs = BuildSpecs(out var referenced);

            _ = Task.Run(() =>
            {
                try
                {
                    var entries = Materialize(specs);
                    _cache.Trim(referenced);
                    _source.SetEntries(entries);
                    UpdateEnabled();
                    if (_source.IsActive)
                        _scheduler.RequestFrame(_source);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SideDisplayAnimationManager: rescan failed: {ex.Message}");
                }
            });
        });
    }

    /// <summary>UI-thread pass: reads the current left/right strip canvases and (re)subscribes to their
    /// change events so a live edit restarts the animation immediately.</summary>
    private List<Spec> BuildSpecs(out HashSet<string> referenced)
    {
        referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new List<Spec>();

        // Only devices that page their dial columns independently have addressable side strips
        // (Razer). On single-column devices there is nothing to animate — drop any subscriptions
        // and return an empty set so the source stays inactive.
        if (!_pageManager.HasIndependentRotarySides)
        {
            for (var slot = 0; slot < 2; slot++)
                SubscribeCanvas(slot, null);
            return specs;
        }

        for (var slot = 0; slot < 2; slot++)
        {
            var side = slot == 0 ? RotarySide.Left : RotarySide.Right;
            var page = _pageManager.GetCurrentRotaryPage(side);

            // Only a FreeDraw strip renders its canvas; Segmented/PluginOverride ignore StripCanvas
            // even if a stale one exists.
            var canvas = page is { StripMode: StripMode.FreeDraw } ? page.StripCanvas : null;

            SubscribeCanvas(slot, canvas);

            if (canvas?.Layers == null) continue;

            foreach (var layer in canvas.Layers)
            {
                if (layer is ImageLayer { IsAnimated: true } img &&
                    !string.IsNullOrWhiteSpace(img.AnimatedAssetPath))
                {
                    specs.Add(new Spec { Side = side, ImageLayer = img, AnimPath = img.AnimatedAssetPath });
                    referenced.Add(img.AnimatedAssetPath);
                }
            }
        }

        return specs;
    }

    /// <summary>Background pass: decodes animated images (cached) and builds the source entries.</summary>
    private List<SideDisplayAnimationSource.SideEntry> Materialize(List<Spec> specs)
    {
        var entries = new List<SideDisplayAnimationSource.SideEntry>(specs.Count);

        foreach (var spec in specs)
        {
            var anim = _cache.Get(spec.AnimPath);
            if (anim == null) continue;

            // Seed the first frame so a strip redraw shows content immediately, before the source's
            // first tick (no blank flash). Field swap, no notify — same as the button source.
            if (spec.ImageLayer.CachedImage == null && anim.Frames.Length > 0)
                spec.ImageLayer.SetAnimationFrame(anim.Frames[0]);

            entries.Add(new SideDisplayAnimationSource.SideEntry
            {
                Side = spec.Side,
                Layer = spec.ImageLayer,
                Anim = anim,
                DesiredFps = ImageFps
            });
        }

        return entries;
    }

    // ── live-edit subscription ──────────────────────────────────────────────────

    /// <summary>Reconciles the ItemChanged subscription for one side's strip canvas (UI thread).</summary>
    private void SubscribeCanvas(int slot, TouchButton canvas)
    {
        var current = _subscribedCanvases[slot];
        if (ReferenceEquals(current, canvas)) return;

        if (current != null) current.ItemChanged -= OnStripCanvasItemChanged;
        _subscribedCanvases[slot] = canvas;
        if (canvas != null) canvas.ItemChanged += OnStripCanvasItemChanged;
    }

    // A layer edit (add/remove/retarget) rebuilds the entry set; the animation frame swap itself does
    // not raise ItemChanged (SetAnimationFrame is a silent field swap), so this never self-triggers.
    private void OnStripCanvasItemChanged(object sender, EventArgs e) => Rescan();

    // ── takeover / pause coordination ──────────────────────────────────────────

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

    private void OnTakeoverStateChanged()
    {
        UpdateEnabled();
        if (_source.IsActive)
            _scheduler.RequestFrame(_source);
    }

    /// <summary>Enabled only when no other feature owns the display (mirrors the controller's veto).</summary>
    private void UpdateEnabled()
    {
        var enabled = !_screensaverActive && !_exclusiveMode.IsActive && !_folderNav.IsActive;
        _source.SetEnabled(enabled);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _pageManager.OnRotaryPageChanged -= OnRotaryPageChanged;
        _screensaver.Started -= OnScreensaverStarted;
        _screensaver.Stopped -= OnScreensaverStopped;
        _exclusiveMode.StateChanged -= OnTakeoverStateChanged;
        _folderNav.StateChanged -= OnTakeoverStateChanged;

        for (var slot = 0; slot < 2; slot++)
            SubscribeCanvas(slot, null);

        try { _scheduler.Unregister(_source); } catch { /* best effort */ }
        try { _source.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>Intermediate captured on the UI thread before the background decode pass.</summary>
    private sealed class Spec
    {
        public RotarySide Side { get; init; }
        public ImageLayer ImageLayer { get; init; }
        public string AnimPath { get; init; }
    }
}
