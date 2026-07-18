using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Animation;

namespace LoupixDeck.Services.Plugins;

/// <inheritdoc cref="IFullDisplayRenderService"/>
public sealed class FullDisplayRenderService : IFullDisplayRenderService
{
    private readonly IDeviceService _deviceService;
    private readonly IAnimationScheduler _scheduler;
    private readonly IExclusiveModeService _exclusiveMode;

    // Single-owner state. The lock guards the transition; the actual rendering happens on the
    // scheduler thread and reads volatile state.
    private readonly Lock _gate = new();
    private Session _current;
    private int _previousFpsLimit;

    public FullDisplayRenderService(
        IDeviceService deviceService,
        IAnimationScheduler scheduler,
        IExclusiveModeService exclusiveMode)
    {
        _deviceService = deviceService;
        _scheduler = scheduler;
        _exclusiveMode = exclusiveMode;
    }

    public bool IsActive
    {
        get { lock (_gate) return _current != null; }
    }

    public event Action Started;
    public event Action Stopped;

    public IFullDisplayRenderSession TryEnter(IFullDisplayRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        PluginFullDisplayAnimationSource source;
        Session session;

        lock (_gate)
        {
            if (_current != null)
                return null;

            // Compose with exclusive mode: whoever owns the display first wins (no stealing). The
            // reverse guard (exclusive rejected while full-display is active) lives in the plugin
            // host wiring so ExclusiveModeService keeps no dependency on this service.
            if (_exclusiveMode.IsActive)
                return null;

            var device = _deviceService.Device;
            if (device == null)
                return null;

            source = new PluginFullDisplayAnimationSource(device, renderer);
            if (source.TargetCount == 0)
            {
                source.Dispose();
                return null;
            }

            session = new Session(this, source);
            _current = session;
        }

        // Outside the lock (mirrors ScreensaverManager): OnStart may launch an external decoder, so
        // don't hold the transition lock across it.
        if (!source.Start())
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, session))
                    _current = null;
            }
            source.Dispose();
            return null;
        }

        // Suppress the controller's own rendering first, then raise the global cap so the target
        // rate isn't clamped, then start ticking the source — the same order the screensaver uses.
        RaiseStarted();
        _previousFpsLimit = _scheduler.GlobalFpsLimit;
        var fps = renderer.TargetFps > 0 ? renderer.TargetFps : _previousFpsLimit;
        _scheduler.SetGlobalFpsLimit(fps);
        _scheduler.Register(source);

        return session;
    }

    public void SetPaused(bool paused)
    {
        Session current;
        lock (_gate) current = _current;
        current?.Source.SetPaused(paused);
    }

    private void Exit(Session session)
    {
        PluginFullDisplayAnimationSource source;
        lock (_gate)
        {
            if (_current == null || !ReferenceEquals(_current, session))
                return;

            source = session.Source;
            _current = null;
        }

        try { _scheduler.Unregister(source); } catch { /* best effort */ }
        try { source.Dispose(); } catch { /* best effort */ }

        // Restore the scheduler's global FPS cap we raised on enter.
        if (_previousFpsLimit > 0)
            try { _scheduler.SetGlobalFpsLimit(_previousFpsLimit); } catch { /* best effort */ }

        RaiseStopped();
    }

    private bool IsCurrent(Session session)
    {
        lock (_gate) return ReferenceEquals(_current, session);
    }

    private void RaiseStarted()
    {
        try { Started?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"[PluginFullDisplay] Started handler threw: {ex.Message}"); }
    }

    private void RaiseStopped()
    {
        try { Stopped?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"[PluginFullDisplay] Stopped handler threw: {ex.Message}"); }
    }

    /// <summary>Ownership handle handed back to the plugin. Releasing is idempotent and routes back
    /// through <see cref="Exit"/>.</summary>
    private sealed class Session : IFullDisplayRenderSession
    {
        private readonly FullDisplayRenderService _owner;
        private int _released;

        public Session(FullDisplayRenderService owner, PluginFullDisplayAnimationSource source)
        {
            _owner = owner;
            Source = source;
        }

        public PluginFullDisplayAnimationSource Source { get; }

        public bool IsActive => Volatile.Read(ref _released) == 0 && _owner.IsCurrent(this);

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;
            _owner.Exit(this);
        }

        public void Dispose() => Release();
    }
}
