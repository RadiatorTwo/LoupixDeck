using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Screensaver;
using LoupixDeck.Utils;

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
        IFullDisplayRenderService fullDisplay)
    {
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

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _pageManager.OnTouchPageChanged += OnTouchPageChanged;
        _screensaver.Started += OnScreensaverStarted;
        _screensaver.Stopped += OnScreensaverStopped;
        _exclusiveMode.StateChanged += OnTakeoverStateChanged;
        _folderNav.StateChanged += OnTakeoverStateChanged;
        _fullDisplay.Started += OnTakeoverStateChanged;
        _fullDisplay.Stopped += OnTakeoverStateChanged;

        Rebuild();
    }

    public void InvalidateButton(int index) => _source?.InvalidateButton(index);

    public void InvalidateAllButtons() => _source?.InvalidateAllButtons();

    private void OnTouchPageChanged(int previous, int current) => Rebuild();

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
        if (path == _playingPath && slot.VideoFps == _playingFps) return;

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

        Stop();

        if (slot is not { HasVideo: true }) return;

        var device = _deviceService.Device;
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

        var source = new WallpaperAnimationSource(device, _config, slot.VideoPath, slot.VideoFps);
        if (!source.Start())
        {
            source.Dispose();
            return;
        }

        _source = source;
        _playingPath = slot.VideoPath;
        _playingFps = slot.VideoFps;
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
        var enabled = !_screensaverActive &&
                      !_folderNav.IsActive &&
                      !_fullDisplay.IsActive &&
                      !_exclusiveMode.Owns(ExclusiveControlScope.TouchButtons);
        _source?.SetEnabled(enabled);
    }

    private void RequestFrame()
    {
        var source = _source;
        if (source is { IsActive: true }) _scheduler.RequestFrame(source);
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

        _pageManager.OnTouchPageChanged -= OnTouchPageChanged;
        _screensaver.Started -= OnScreensaverStarted;
        _screensaver.Stopped -= OnScreensaverStopped;
        _exclusiveMode.StateChanged -= OnTakeoverStateChanged;
        _folderNav.StateChanged -= OnTakeoverStateChanged;
        _fullDisplay.Started -= OnTakeoverStateChanged;
        _fullDisplay.Stopped -= OnTakeoverStateChanged;

        WatchSlot(null);
        Stop();
    }
}
