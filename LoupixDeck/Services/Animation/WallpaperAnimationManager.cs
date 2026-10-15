using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Screensaver;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="IWallpaperAnimationManager"/>
/// <remarks>
/// Mirrors <see cref="ButtonAnimationManager"/>: it owns one <see cref="IAnimationSource"/> per
/// device on the central scheduler and never renders anything itself. It differs in what it
/// watches — the active page's main wallpaper slot rather than the page's layers — and in how hard
/// it yields, because a video wallpaper owns the whole panel rather than single keys: a screensaver,
/// a plugin full-display takeover, an exclusive grab of the touch grid and folder navigation all
/// pause it.
///
/// Playback is per page. Switching to a page whose wallpaper is a still image (or none) tears the
/// clip down, which also stops its ffmpeg process — a device only pays for a video wallpaper on the
/// pages that use one.
/// </remarks>
public sealed class WallpaperAnimationManager : IWallpaperAnimationManager, IDisposable
{
    private readonly IPageManager _pageManager;
    private readonly IDeviceService _deviceService;
    private readonly LoupedeckConfig _config;
    private readonly IAnimationScheduler _scheduler;
    private readonly IScreensaverManager _screensaver;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFolderNavigationService _folderNav;
    private readonly IFullDisplayRenderService _fullDisplay;

    // Resolves the device controller lazily: it depends on this manager, so it cannot be injected.
    private readonly IServiceProvider _deviceProvider;

    private readonly Lock _gate = new();
    private bool _started;
    private bool _disposed;
    private volatile bool _screensaverActive;

    private WallpaperAnimationSource _source;

    // The slot we are subscribed to, and the clip identity we started for. The slot raises Changed
    // for every parameter including the opacity slider, so playback is only rebuilt when the clip
    // itself changed — the opacity is read live by the overlay on each frame.
    private WallpaperSlot _watchedSlot;
    private string _playingPath;
    private int _playingFps;
    private BitmapHelper.ScalingOption _playingScaling;

    // So a missing file or a missing ffmpeg is reported once per clip rather than per page change.
    private string _lastReportedProblemPath;

    public WallpaperAnimationManager(
        IPageManager pageManager,
        IDeviceService deviceService,
        LoupedeckConfig config,
        IAnimationScheduler scheduler,
        IScreensaverManager screensaver,
        IExclusiveModeService exclusiveMode,
        IFolderNavigationService folderNav,
        IFullDisplayRenderService fullDisplay,
        IServiceProvider deviceProvider)
    {
        _deviceProvider = deviceProvider;
        _pageManager = pageManager;
        _deviceService = deviceService;
        _config = config;
        _scheduler = scheduler;
        _screensaver = screensaver;
        _exclusiveMode = exclusiveMode;
        _folderNav = folderNav;
        _fullDisplay = fullDisplay;
    }

    public bool IsPlaying => _source != null;

    public bool CarriesStrips => _source is { IsActive: true };

    public event Action StripOwnerChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _pageManager.TouchLayoutChanged += OnTouchLayoutChanged;
        _screensaver.Started += OnScreensaverStarted;
        _screensaver.Stopped += OnScreensaverStopped;
        _exclusiveMode.StateChanged += OnTakeoverStateChanged;
        _folderNav.StateChanged += OnTakeoverStateChanged;
        _fullDisplay.Started += OnTakeoverStateChanged;
        _fullDisplay.Stopped += OnTakeoverStateChanged;
        _deviceService.ClipKeyRedraw = RedirectTemporaryTextKey;

        Rebuild();
    }

    public bool TryRedirectButtonRedraw(int index)
    {
        var source = _source;
        if (source is not { IsActive: true }) return false;

        source.InvalidateButton(index);
        _scheduler.RequestFrame(source);
        RefreshUiMirror(source, _config?.CurrentTouchButtonPage?.TouchButtons?.FindByIndex(index));
        return true;
    }

    /// <summary>
    /// <see cref="IDeviceService.ClipKeyRedraw"/>: while a clip plays, the page-name tile and the
    /// key it covered go into the clip's frames. Showing it only asks for a frame — the overlay
    /// draws the tile itself; taking it down re-renders the key, whose redraws were held back.
    /// </summary>
    private bool RedirectTemporaryTextKey(int index, bool showing)
    {
        var source = _source;
        if (source is not { IsActive: true }) return false;

        if (showing)
        {
            source.MarkOverlayDirty();
            _scheduler.RequestFrame(source);
            return true;
        }

        return TryRedirectButtonRedraw(index);
    }

    public bool TryRedirectPageRedraw()
    {
        var source = _source;
        if (source is not { IsActive: true }) return false;

        source.InvalidateAllButtons();
        _scheduler.RequestFrame(source);

        var buttons = _config?.CurrentTouchButtonPage?.TouchButtons;
        if (buttons != null)
        {
            foreach (var button in buttons) RefreshUiMirror(source, button);
        }

        return true;
    }

    public bool TryRedirectStripRedraw(RotarySide side, SKBitmap foreground)
    {
        var source = _source;
        if (source is not { IsActive: true })
        {
            foreground?.Dispose();
            return false;
        }

        source.SetStrip(side, foreground);
        _scheduler.RequestFrame(source);
        return true;
    }

    /// <summary>
    /// Re-renders one key's UI mirror (<see cref="TouchButton.RenderedImage"/>).
    ///
    /// The redirect above hands the key to the video overlay, which reaches the device but never
    /// touches that bitmap — only <c>DrawTouchButton</c> publishes it, and the redirect is what
    /// replaces that call. Without this the window keeps showing the key as it looked before the
    /// edit for as long as the clip plays, while the device already shows the new layers.
    ///
    /// The layers are rendered once, into the source's foreground cache, and the mirror is built
    /// from that same bitmap: the next video frame then blits it without drawing the layers again.
    /// </summary>
    private void RefreshUiMirror(WallpaperAnimationSource source, TouchButton button)
    {
        var device = _deviceService?.Device;
        if (button == null || device == null || _config == null) return;

        BitmapHelper.RenderTouchButtonOverForeground(button, _config, device.KeySize, device.KeySize,
            device.GetWallpaperKeyRect(button.Index), () => source.GetForeground(button));
    }

    private void OnTouchLayoutChanged() => Rebuild();

    private void OnScreensaverStarted()
    {
        _screensaverActive = true;
        UpdateEnabled();
    }

    private void OnScreensaverStopped()
    {
        _screensaverActive = false;
        UpdateEnabled();
        RequestFrame();
    }

    private void OnTakeoverStateChanged()
    {
        UpdateEnabled();
        RequestFrame();
    }

    /// <summary>
    /// Fires for every slot parameter, so it only restarts playback when the clip identity changed.
    /// An opacity or scaling change needs nothing: the overlay reads those live per frame.
    /// </summary>
    private void OnWatchedSlotChanged(object sender, EventArgs e)
    {
        var slot = _watchedSlot;
        if (slot == null) return;

        var path = slot.HasVideo ? slot.VideoPath : null;
        if (path == _playingPath &&
            slot.VideoFps == _playingFps &&
            slot.ScalingOption == _playingScaling)
        {
            // A paused or single-frame clip sends no new frame on its own to show the change.
            RequestFrame();
            return;
        }

        // The fps and the fit are baked into ffmpeg's arguments at spawn, so either one changing
        // means a restart. Opacity is not: the overlay reads it live per frame.
        Rebuild();
    }

    /// <summary>
    /// Brings playback in line with the active page: starts the page's clip, or tears playback down
    /// when the page has none, cannot be played, or ffmpeg is unavailable — in which case the still
    /// wallpaper on the slot keeps rendering exactly as before.
    /// </summary>
    private void Rebuild()
    {
        if (_disposed) return;

        var slot = _pageManager.CurrentTouchButtonPage?.MainWallpaper;
        WatchSlot(slot);

        var wasCarryingStrips = CarriesStrips;
        try
        {
            RebuildCore(slot);
        }
        finally
        {
            // A fresh source starts without strips, and a stopped one leaves the strips showing
            // the last video frame behind them: either way they need a redraw by their new owner.
            if (wasCarryingStrips || CarriesStrips) StripOwnerChanged?.Invoke();
        }
    }

    private void RebuildCore(WallpaperSlot slot)
    {
        var device = _deviceService.Device;

        // The new page plays the same clip the same way: keep it running and only bring the keys
        // over, rather than killing ffmpeg and decoding the clip into a fresh ring again.
        var running = _source;
        if (running is { IsRunning: true } && device != null && ReferenceEquals(running.Device, device) &&
            slot is { HasVideo: true } && slot.VideoPath == _playingPath &&
            slot.VideoFps == _playingFps && slot.ScalingOption == _playingScaling)
        {
            running.Retarget(slot);
            running.InvalidateAllButtons();
            UpdateEnabled();
            RequestFrame();
            return;
        }

        Stop();

        if (slot is not { HasVideo: true }) return;

        if (device == null) return;

        if (!File.Exists(slot.VideoPath))
        {
            ReportProblem(slot.VideoPath, $"[Wallpaper] clip not found: {slot.VideoPath}");
            return;
        }

        if (!FfmpegDetector.IsAvailable())
        {
            ReportProblem(slot.VideoPath,
                "[Wallpaper] ffmpeg not found on PATH — falling back to the still wallpaper.");
            return;
        }

        var source = new WallpaperAnimationSource(device, _config, slot, MayPushFrame,
            _deviceService.GetTemporaryTextTile, slot.VideoPath,
            slot.VideoFps, slot.ScalingOption);
        if (!source.Start())
        {
            source.Dispose();
            return;
        }

        _source = source;
        _playingPath = slot.VideoPath;
        _playingFps = slot.VideoFps;
        _playingScaling = slot.ScalingOption;
        _lastReportedProblemPath = null;

        UpdateEnabled();
        _scheduler.Register(source);
        Console.WriteLine($"[Wallpaper] playing '{slot.VideoName ?? slot.VideoPath}' at {slot.VideoFps} fps.");
    }

    private void WatchSlot(WallpaperSlot slot)
    {
        if (ReferenceEquals(_watchedSlot, slot)) return;

        if (_watchedSlot != null) _watchedSlot.Changed -= OnWatchedSlotChanged;
        _watchedSlot = slot;
        if (_watchedSlot != null) _watchedSlot.Changed += OnWatchedSlotChanged;
    }

    /// <summary>Unregisters and disposes the running source, which also kills its ffmpeg.</summary>
    private void Stop()
    {
        var source = _source;
        _source = null;
        _playingPath = null;
        _playingFps = 0;

        if (source == null) return;

        try { _scheduler.Unregister(source); } catch { /* best effort */ }
        try { source.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>
    /// Paused whenever something else owns the display. Stricter than the per-button animations'
    /// veto: those only stand back for the touch grid, while a video wallpaper writes the whole
    /// panel and so must also yield to a plugin full-display takeover.
    /// </summary>
    private void UpdateEnabled()
    {
        var enabled = IsDisplayFree();

        var wasCarryingStrips = CarriesStrips;
        _source?.SetEnabled(enabled);

        // Paused, the strips go back to being pushed on their own, with their background; resumed,
        // the overlay needs them again.
        if (wasCarryingStrips != CarriesStrips) StripOwnerChanged?.Invoke();
    }

    /// <summary>True while nothing else owns the display. Read live by the playing source before
    /// every frame, so a frame never lands over a takeover that has not reached
    /// <see cref="UpdateEnabled"/> yet.</summary>
    private bool IsDisplayFree() =>
        // A device switched off (by the user, a suspend or the monitors going dark) shows nothing,
        // so a frame pushed to it is serial bandwidth and ffmpeg work spent on a blank panel.
        // Switching it back on repaints the page, which hands the redraw to the clip again.
        _deviceProvider.GetService<Controllers.IDeviceController>()?.IsDeviceOff != true &&
        !_screensaverActive &&
        !_folderNav.IsActive &&
        !_fullDisplay.IsActive &&
        !_exclusiveMode.Owns(ExclusiveControlScope.TouchButtons);

    /// <summary>
    /// The source's live check before each frame: <see cref="IsDisplayFree"/>, and no touch page
    /// slide in flight. A clip kept playing across a page change would otherwise push frames
    /// between the slide's own. Kept out of <see cref="UpdateEnabled"/>, which nothing calls again
    /// when the slide lands: a vetoed frame leaves the overlay dirty, so the next tick pushes.
    /// </summary>
    private bool MayPushFrame() =>
        IsDisplayFree() &&
        _deviceProvider.GetService<Controllers.IDeviceController>()?.TouchSlideInFlight != true;

    private void RequestFrame()
    {
        var source = _source;
        if (source is not { IsActive: true }) return;

        source.MarkOverlayDirty();
        _scheduler.RequestFrame(source);
    }

    private void ReportProblem(string path, string message)
    {
        if (_lastReportedProblemPath == path) return;
        _lastReportedProblemPath = path;
        Console.WriteLine(message);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _pageManager.TouchLayoutChanged -= OnTouchLayoutChanged;
        _screensaver.Started -= OnScreensaverStarted;
        _screensaver.Stopped -= OnScreensaverStopped;
        _exclusiveMode.StateChanged -= OnTakeoverStateChanged;
        _folderNav.StateChanged -= OnTakeoverStateChanged;
        _fullDisplay.Started -= OnTakeoverStateChanged;
        _fullDisplay.Stopped -= OnTakeoverStateChanged;
        if (_deviceService.ClipKeyRedraw == RedirectTemporaryTextKey) _deviceService.ClipKeyRedraw = null;

        WatchSlot(null);
        Stop();
    }
}
