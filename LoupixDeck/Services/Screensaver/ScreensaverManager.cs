using System.ComponentModel;
using LoupixDeck.Models;
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
    private int _previousFpsLimit;
    private bool _armed;
    private bool _disposed;

    public event Action Started;
    public event Action Stopped;

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

    public bool NotifyActivity()
    {
        // Stop a running screensaver off the calling (serial-read) thread so killing
        // ffmpeg never stalls input handling, then re-arm the idle countdown.
        var wasRunning = IsRunning;
        if (wasRunning)
            _ = Task.Run(StopScreensaver);

        RestartIdleTimer();

        // When this input woke the screensaver, the caller consumes it (no normal action).
        return wasRunning;
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

            // Don't start over a plugin takeover (exclusive mode) or folder navigation —
            // we only READ those states here; the screensaver never enters exclusive mode
            // itself (that mode is reserved for plugin takeovers).
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

            var source = new ScreensaverAnimationSource(
                device, absolute, _config.ScreensaverFps, _config.ScreensaverLoop,
                onEnded: () => _ = Task.Run(StopScreensaver));

            if (!source.Start())
                return;

            lock (_gate)
            {
                if (_disposed || !_armed)
                {
                    // Disarmed while we were starting — unwind.
                    source.Dispose();
                    return;
                }

                _source = source;
            }

            // Tell the controller to suppress its own rendering while the screensaver owns
            // the display (and stop side-strip provider timers).
            RaiseStarted();

            // Raise the scheduler's global cap to the screensaver's FPS so a rate above the
            // default limit isn't clamped. Safe because the screensaver is the only source
            // drawing while it runs; the previous cap is restored on stop.
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
        lock (_gate)
        {
            source = _source;
            _source = null;
        }

        if (source == null) return;

        try { _scheduler.Unregister(source); } catch { /* best effort */ }
        try { source.Dispose(); } catch { /* best effort */ }
        // Restore the scheduler's global FPS cap we raised on start.
        if (_previousFpsLimit > 0)
            try { _scheduler.SetGlobalFpsLimit(_previousFpsLimit); } catch { /* best effort */ }

        // Tell the controller to repaint the active page (it owned the display while we ran).
        RaiseStopped();

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

            case nameof(LoupedeckConfig.ScreensaverVideoPath):
                // The clip changed — stop a running screensaver so the next idle trigger
                // picks up the new video instead of continuing to play the old one.
                if (IsRunning)
                    _ = Task.Run(StopScreensaver);
                // Re-arm the idle countdown. Stopping above leaves the one-shot idle timer
                // unscheduled (it doesn't re-arm itself while the screensaver runs), so
                // without this the screensaver would stay off until the next device input.
                // Now it restarts with the new clip after the idle timeout.
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

    private void RaiseStarted()
    {
        try { Started?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"[Screensaver] Started handler threw: {ex.Message}"); }
    }

    private void RaiseStopped()
    {
        try { Stopped?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"[Screensaver] Stopped handler threw: {ex.Message}"); }
    }
}
