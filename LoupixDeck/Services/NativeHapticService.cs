using System.Collections.Specialized;
using System.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;

namespace LoupixDeck.Services;

public interface INativeHapticService
{
    void Apply();
}

public sealed class NativeHapticService : INativeHapticService, IDisposable
{
    private readonly LoupedeckConfig _config;
    private readonly IDeviceService _deviceService;
    private readonly IPageManager _pageManager;
    private readonly System.Timers.Timer _debounce;
    private readonly Lock _lock = new();

    private static readonly HashSet<string> HapticProps =
    [
        nameof(LoupedeckConfig.HapticEnabled)
    ];

    private readonly List<TouchButton> _watchedButtons = [];
    private readonly List<HapticStep> _watchedSteps = [];

    public NativeHapticService(LoupedeckConfig config, IDeviceService deviceService, IPageManager pageManager)
    {
        _config = config;
        _deviceService = deviceService;
        _pageManager = pageManager;

        _debounce = new System.Timers.Timer(150) { AutoReset = false };
        _debounce.Elapsed += (_, _) => SendNow();

        _config.PropertyChanged += OnConfigChanged;
        _pageManager.OnTouchPageChanged += (_, _) => { RebindCurrentPageButtons(); Schedule(); };
        RebindCurrentPageButtons();

        _config.HapticSteps.CollectionChanged += OnStepsChanged;
        RebindSteps();
    }

    private void RebindCurrentPageButtons()
    {
        foreach (var b in _watchedButtons)
            b.PropertyChanged -= OnTouchButtonChanged;
        _watchedButtons.Clear();

        var page = _config.CurrentTouchButtonPage;
        if (page?.TouchButtons == null) return;
        foreach (var b in page.TouchButtons)
        {
            if (b == null) continue;
            b.PropertyChanged += OnTouchButtonChanged;
            _watchedButtons.Add(b);
        }
    }

    private void OnTouchButtonChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TouchButton.VibrationEnabled) ||
            e.PropertyName == nameof(TouchButton.VibrationPattern))
        {
            Schedule();
        }
    }

    private void OnStepsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        RebindSteps();
        Schedule();
    }

    private void RebindSteps()
    {
        foreach (var s in _watchedSteps)
            s.PropertyChanged -= OnStepChanged;
        _watchedSteps.Clear();

        foreach (var s in _config.HapticSteps)
        {
            if (s == null) continue;
            s.PropertyChanged += OnStepChanged;
            _watchedSteps.Add(s);
        }
    }

    private void OnStepChanged(object sender, PropertyChangedEventArgs e) => Schedule();

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && HapticProps.Contains(e.PropertyName))
            Schedule();
    }

    public void Apply() => Schedule();

    private void Schedule()
    {
        lock (_lock)
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private void SendNow()
    {
        // Firmware-side native haptic (op-code 0x2e) is intentionally NOT used: on the
        // hardware it never played a touch pulse even with byte-correct frames (verified
        // against Athorus' reference from issue #101), and its disable command wedges /
        // freezes the device. Haptic feedback now runs entirely through the software
        // Vibrate() pulse (0x1b) from the touch handler, which fires immediately on touch.
        //
        // This service is kept (config/page/step subscriptions still fire) but performs no
        // device I/O, so it can neither program nor disable the firmware haptic engine.
    }

    public void Dispose()
    {
        _config.PropertyChanged -= OnConfigChanged;
        _config.HapticSteps.CollectionChanged -= OnStepsChanged;
        foreach (var b in _watchedButtons)
            b.PropertyChanged -= OnTouchButtonChanged;
        foreach (var s in _watchedSteps)
            s.PropertyChanged -= OnStepChanged;
        _watchedButtons.Clear();
        _watchedSteps.Clear();
        _debounce.Dispose();
    }
}
