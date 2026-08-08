using System.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.Services.Animation;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Screensaver;

/// <inheritdoc cref="IScreensaverManager"/>
public sealed class ScreensaverManager : IScreensaverManager, IDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFullDisplayRenderService _fullDisplay;
    private readonly IAnimationScheduler _scheduler;
    private readonly IAssetService _assetService;
    private readonly IFolderNavigationService _folderNav;
    private readonly IScreensaverProviderRegistry _providerRegistry;
    private readonly LoupedeckConfig _config;

    // Floor on the idle timeout so a mistyped tiny value can't make the screensaver
    // fire almost immediately after every interaction.
    private const int MinIdleSeconds = 5;

    private readonly object _gate = new();
    private readonly Timer _idleTimer;

    // Either a ScreensaverAnimationSource (video clip) or a PluginFullDisplayAnimationSource
    // (plugin provider) — both are IAnimationSource + IDisposable, and nothing here needs more.
    private IAnimationSource _source;
    // Id of the provider behind a running plugin screensaver; null on the video path. Lets us stop
    // the screensaver when that provider disappears from the registry.
    private string _runningPluginId;
    private int _previousFpsLimit;
    private bool _armed;
    private bool _disposed;

    public event Action Started;
    public event Action Stopped;

    public ScreensaverManager(
        IDeviceService deviceService,
        IExclusiveModeService exclusiveMode,
        IFullDisplayRenderService fullDisplay,
        IAnimationScheduler scheduler,
        IAssetService assetService,
        IFolderNavigationService folderNav,
        IScreensaverProviderRegistry providerRegistry,
        LoupedeckConfig config)
    {
        _deviceService = deviceService;
        _exclusiveMode = exclusiveMode;
        _fullDisplay = fullDisplay;
        _scheduler = scheduler;
        _assetService = assetService;
        _folderNav = folderNav;
        _providerRegistry = providerRegistry;
        _config = config;

        _idleTimer = new Timer(_ => OnIdleElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _config.PropertyChanged += OnConfigChanged;

        // A plugin full-display takeover pre-empts a running screensaver (the screensaver is the
        // lowest-priority display owner). Stop it off the caller's thread so killing ffmpeg never
        // stalls the plugin's enter path.
        _fullDisplay.Started += OnFullDisplayStarted;

        // Safety net for a plugin screensaver whose provider goes away while it plays (plugin
        // disabled, removed or reloaded). The reload path also stops us explicitly; this catches
        // every other rebuild, including the ones triggered on another device's reload service.
        _providerRegistry.ProvidersChanged += OnProvidersChanged;
    }

    private void OnFullDisplayStarted()
    {
        if (IsRunning)
            _ = Task.Run(StopScreensaver);
    }

    private void OnProvidersChanged()
    {
        string runningId;
        lock (_gate) runningId = _runningPluginId;

        if (runningId != null && _providerRegistry.Get(runningId) == null)
            StopRunning();
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

    public void StopRunning()
    {
        StopScreensaver();
        RestartIdleTimer();
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

            // Don't start over a plugin takeover (exclusive mode or a full-display renderer) or
            // folder navigation — we only READ those states here; the screensaver never enters
            // those modes itself (they're reserved for plugin takeovers).
            if (_exclusiveMode.IsActive || _fullDisplay.IsActive || _folderNav.IsActive) return;

            // Build (and start) the configured source. Both branches start outside the lock,
            // because a start touches ffmpeg / plugin code that must not run under _gate.
            var (source, fpsCap, pluginId) = _config.ScreensaverSource == ScreensaverSourceKind.Plugin
                ? TryStartPlugin(device)
                : TryStartVideo(device);

            if (source == null)
                return;

            lock (_gate)
            {
                if (_disposed || !_armed)
                {
                    // Disarmed while we were starting — unwind.
                    (source as IDisposable)?.Dispose();
                    return;
                }

                _source = source;
                _runningPluginId = pluginId;
            }

            // Tell the controller to suppress its own rendering while the screensaver owns
            // the display (and stop side-strip provider timers).
            RaiseStarted();

            // Raise the scheduler's global cap to the screensaver's FPS so a rate above the
            // default limit isn't clamped. Safe because the screensaver is the only source
            // drawing while it runs; the previous cap is restored on stop.
            _previousFpsLimit = _scheduler.GlobalFpsLimit;
            _scheduler.SetGlobalFpsLimit(fpsCap);

            _scheduler.Register(source);
            Console.WriteLine("[Screensaver] started.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screensaver] start failed: {ex.Message}");
        }
    }

    /// <summary>Builds the video-clip source (the pre-#124 behavior). ffmpeg is only required —
    /// and only probed — here.</summary>
    private (IAnimationSource Source, int FpsCap, string PluginId) TryStartVideo(
        LoupedeckDevice.Device.LoupedeckDevice device)
    {
        var absolute = _assetService.ResolveAbsolute(_config.ScreensaverVideoPath);
        if (string.IsNullOrWhiteSpace(absolute) || !File.Exists(absolute))
        {
            Console.WriteLine("[Screensaver] no playable video configured.");
            return default;
        }

        if (!FfmpegDetector.IsAvailable())
        {
            Console.WriteLine("[Screensaver] ffmpeg not found on PATH — feature unavailable.");
            return default;
        }

        var source = new ScreensaverAnimationSource(
            device, absolute, _config.ScreensaverFps, _config.ScreensaverLoop,
            onEnded: () => _ = Task.Run(StopScreensaver));

        return source.Start() ? (source, _config.ScreensaverFps, null) : default;
    }

    /// <summary>
    /// Builds a source from the configured plugin provider (issue #124). Mirrors
    /// <see cref="Plugins.FullDisplayRenderService"/>'s enter path — including starting outside the
    /// lock — but the host, not the plugin, owns the renderer's lifetime here. A missing provider is
    /// not an error: the screensaver is simply skipped and the active page stays on screen.
    /// </summary>
    private (IAnimationSource Source, int FpsCap, string PluginId) TryStartPlugin(
        LoupedeckDevice.Device.LoupedeckDevice device)
    {
        var provider = _providerRegistry.Get(_config.ScreensaverPluginId);
        if (provider == null)
        {
            Console.WriteLine(
                $"[Screensaver] no screensaver provider '{_config.ScreensaverPluginId}' loaded.");
            return default;
        }

        IFullDisplayRenderer renderer;
        try
        {
            renderer = provider.CreateRenderer();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screensaver] '{provider.Id}' CreateRenderer threw: {ex.Message}");
            return default;
        }

        if (renderer == null)
        {
            Console.WriteLine($"[Screensaver] '{provider.Id}' declined to create a renderer.");
            return default;
        }

        // A one-shot plugin screensaver ends the screensaver, exactly like a non-looping clip.
        var source = new PluginFullDisplayAnimationSource(
            device, renderer, onEnded: () => _ = Task.Run(StopScreensaver));

        if (source.TargetCount == 0 || !source.Start())
        {
            source.Dispose();
            return default;
        }

        // The plugin's declared rate wins; the configured FPS is the fallback for a renderer
        // that leaves TargetFps at 0 ("use the host's default").
        var fps = renderer.TargetFps > 0 ? renderer.TargetFps : _config.ScreensaverFps;
        return (source, fps, provider.Id);
    }

    private void StopScreensaver()
    {
        IAnimationSource source;
        lock (_gate)
        {
            source = _source;
            _source = null;
            _runningPluginId = null;
        }

        if (source == null) return;

        try { _scheduler.Unregister(source); } catch { /* best effort */ }
        try { (source as IDisposable)?.Dispose(); } catch { /* best effort */ }
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
            case nameof(LoupedeckConfig.ScreensaverSource):
            case nameof(LoupedeckConfig.ScreensaverPluginId):
                // The source changed — stop a running screensaver so the next idle trigger
                // picks up the new clip/provider instead of continuing to play the old one.
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
        _fullDisplay.Started -= OnFullDisplayStarted;
        _providerRegistry.ProvidersChanged -= OnProvidersChanged;
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
