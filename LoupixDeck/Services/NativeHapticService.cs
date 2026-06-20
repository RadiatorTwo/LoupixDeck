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
            Schedule();
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
        var dev = _deviceService.Device;
        if (dev == null) return;

        try
        {
            if (!_config.HapticEnabled)
            {
                dev.DisableNativeHaptic();
                return;
            }

            var page = _config.CurrentTouchButtonPage;
            var steps = _config.HapticSteps;
            if (steps == null || steps.Count == 0)
            {
                dev.DisableNativeHaptic();
                return;
            }

            // Clear any stale per-button slots from a previous config — otherwise removing
            // a step (or enabling a per-button override) leaves the old seq entry alive
            // in firmware.
            dev.DisableNativeHaptic();

            // One frame per button — combined frames with ~30 slots crashed the
            // firmware in testing; per-button frames stay well under that limit.
            // Buttons with their own VibrationEnabled override are skipped here —
            // they are driven via the legacy software Vibrate() path from the
            // touch-start/end handler so the firmware never plays a global pulse
            // on top of the per-button one.
            var btnCount = (byte)Math.Min(dev.TouchButtonCount, byte.MaxValue);
            for (byte i = 0; i < btnCount; i++)
            {
                var btn = page?.TouchButtons?.FindByIndex(i);
                if (btn != null && btn.VibrationEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"[haptic] btn={i} skipped (per-button override)");
                    continue;
                }

                var slots = new List<LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice.HapticSlot>(steps.Count);
                for (var s = 0; s < steps.Count; s++)
                {
                    var step = steps[s];
                    var delay = s == 0 ? (byte)0x04 : step.Delay;
                    slots.Add(new LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice.HapticSlot(
                        i, (byte)s, step.Effect, delay, step.Duration));
                }

                if (slots.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[haptic] btn={i} global slots=[{string.Join(", ", slots.Select(s => $"(fx=0x{s.EffectId:x2},dly={s.DelayMs},dur={s.DurationMs})"))}]");
                    dev.EnableNativeHaptic(slots);
                }
            }
        }
        catch
        {
            // Device may be disconnected — next event will retry.
        }
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
