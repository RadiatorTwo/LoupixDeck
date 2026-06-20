using System.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Animation;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Screensaver;

/// <inheritdoc cref="IScreensaverManager"/>
public sealed class ScreensaverManager : IScreensaverManager, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IAnimationScheduler _scheduler;
    private readonly IAssetService _assetService;
    private readonly IFolderNavigationService _folderNav;
    private readonly LoupedeckConfig _config;

    // Floor on the idle timeout so a mistyped tiny value can't make the screensaver
    // fire almost immediately after every interaction.
    private const int MinIdleSeconds = 5;

    private readonly object _gate = new();
    private readonly Timer _idleTimer;

    private ScreensaverAnimationSource _source;
    private IExclusiveModeProvider _provider;
    private int _previousFpsLimit;
    private bool _armed;
    private bool _disposed;

    public ScreensaverManager(
        IDeviceService deviceService,
        IExclusiveModeService exclusiveMode,
        IAnimationScheduler scheduler,
        IAssetService assetService,
        IFolderNavigationService folderNav,
        LoupedeckConfig config)
    {
        _deviceService = deviceService;
        _exclusiveMode = exclusiveMode;
        _scheduler = scheduler;
        _assetService = assetService;
        _folderNav = folderNav;
        _config = config;

        _idleTimer = new Timer(_ => OnIdleElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _config.PropertyChanged += OnConfigChanged;
    }

    public bool IsRunning
    {
        get { lock (_gate) return _source != null; }
    }

    public bool IsFfmpegAvailable => FfmpegDetector.IsAvailable();

    public void Arm()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _armed = true;
        }
        RestartIdleTimer();
    }

    public void NotifyActivity()
    {
        // Stop a running screensaver off the calling (serial-read) thread so killing
        // ffmpeg never stalls input handling, then re-arm the idle countdown.
        if (IsRunning)
            _ = Task.Run(StopScreensaver);

        RestartIdleTimer();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _armed = false;
        }

        try { _idleTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { /* disposed */ }
        StopScreensaver();
    }

    private void RestartIdleTimer()
    {
        bool armed;
        lock (_gate) armed = _armed && !_disposed;

        if (!armed || !_config.ScreensaverEnabled)
        {
            try { _idleTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { /* disposed */ }
            return;
        }

        var seconds = Math.Max(MinIdleSeconds, _config.ScreensaverIdleTimeoutSeconds);
        try { _idleTimer.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan); }
        catch { /* disposed */ }
    }

    private void OnIdleElapsed() => _ = StartScreensaverAsync();

    private async Task StartScreensaverAsync()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !_armed || _source != null) return;
            }

            if (!_config.ScreensaverEnabled) return;

            var device = _deviceService.Device;
            if (device == null) return;

            // Don't start over a manual device-off, a plugin takeover, or folder navigation.
            if (_exclusiveMode.IsActive || _folderNav.IsActive) return;

            var absolute = _assetService.ResolveAbsolute(_config.ScreensaverVideoPath);
            if (string.IsNullOrWhiteSpace(absolute) || !File.Exists(absolute))
            {
                Console.WriteLine("[Screensaver] no playable video configured.");
                return;
            }

            if (!FfmpegDetector.IsAvailable())
            {
                Console.WriteLine("[Screensaver] ffmpeg not found on PATH — feature unavailable.");
                return;
            }

            // Own the display so page redraws / dynamic text can't interleave with the video.
            var provider = new ScreensaverProvider(NotifyActivity);
            if (!_exclusiveMode.TryEnter(provider))
                return; // another exclusive owner won the race

            var source = new ScreensaverAnimationSource(
                device, absolute, _config.ScreensaverFps, _config.ScreensaverLoop,
                onEnded: () => _ = Task.Run(StopScreensaver));

            if (!source.Start())
            {
                _exclusiveMode.Exit(provider);
                return;
            }

            lock (_gate)
            {
                if (_disposed || !_armed)
                {
                    // Disarmed while we were starting — unwind.
                    source.Dispose();
                    _exclusiveMode.Exit(provider);
                    return;
                }

                _source = source;
                _provider = provider;
            }

            // Raise the scheduler's global cap to the screensaver's FPS so a rate above the
            // default limit isn't clamped. Safe because the screensaver owns the display
            // exclusively (no other source runs); the previous cap is restored on stop.
            _previousFpsLimit = _scheduler.GlobalFpsLimit;
            _scheduler.SetGlobalFpsLimit(_config.ScreensaverFps);

            _scheduler.Register(source);
            Console.WriteLine("[Screensaver] started.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screensaver] start failed: {ex.Message}");
        }
    }

    private void StopScreensaver()
    {
        ScreensaverAnimationSource source;
        IExclusiveModeProvider provider;
        lock (_gate)
        {
            source = _source;
            provider = _provider;
            _source = null;
            _provider = null;
        }

        if (source == null) return;

        try { _scheduler.Unregister(source); } catch { /* best effort */ }
        try { source.Dispose(); } catch { /* best effort */ }
        // Restore the scheduler's global FPS cap we raised on start.
        if (_previousFpsLimit > 0)
            try { _scheduler.SetGlobalFpsLimit(_previousFpsLimit); } catch { /* best effort */ }
        // Leaving exclusive mode makes the controller repaint the active page.
        try { _exclusiveMode.Exit(provider); } catch { /* best effort */ }

        Console.WriteLine("[Screensaver] stopped.");
    }

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoupedeckConfig.ScreensaverEnabled):
                if (!_config.ScreensaverEnabled && IsRunning)
                    _ = Task.Run(StopScreensaver);
                RestartIdleTimer();
                break;

            case nameof(LoupedeckConfig.ScreensaverIdleTimeoutSeconds):
                RestartIdleTimer();
                break;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _armed = false;
        }

        _config.PropertyChanged -= OnConfigChanged;
        StopScreensaver();
        try { _idleTimer.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Minimal exclusive-mode owner for the screensaver: renders nothing itself (the source
    /// pushes frames directly) and routes any hardware input to <see cref="NotifyActivity"/>
    /// as a backstop so the screensaver always stops on interaction.
    /// </summary>
    private sealed class ScreensaverProvider(Action onInput) : IExclusiveModeProvider
    {
        public string Title => "Screensaver";
        public event EventHandler EntriesChanged { add { } remove { } }
        public void OnEnter() { }
        public void OnExit() { }
        public IReadOnlyList<PluginSdk.FolderEntry> BuildTouchEntries() => Array.Empty<PluginSdk.FolderEntry>();
        public void OnSimpleButtonPressed(int index) => onInput();
        public void OnTouchPressed(int slotIndex) => onInput();
        public void OnRotaryPressed(int index) => onInput();
        public void OnRotated(int index, int delta) => onInput();
    }
}
