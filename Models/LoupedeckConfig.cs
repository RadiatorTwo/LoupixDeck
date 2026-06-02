using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

/// <summary>
/// This data model holds all configuration settings,
/// which are loaded and saved via JSON.
/// </summary>
public class LoupedeckConfig : INotifyPropertyChanged
{
    private int _currentRotaryPageIndex = -1;
    private int _currentTouchPageIndex = -1;

    private int _brightness = 100;

    /// <summary>
    /// Schema version of the persisted config. <see cref="ConfigService"/> runs
    /// the migration chain for older versions (see <c>Services/Migrations</c>).
    /// v3 introduced the plugin system: the integration-specific fields were
    /// removed and the per-integration enable flags became <see cref="EnabledPlugins"/>.
    /// </summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    public string DevicePort { get; set; }
    public int DeviceBaudrate { get; set; }

    /// <summary>USB vendor ID of the device this config belongs to (hex, e.g. "2ec2").</summary>
    public string DeviceVid { get; set; }

    /// <summary>USB product ID of the device this config belongs to (hex, e.g. "0006").</summary>
    public string DevicePid { get; set; }

    public int StartupTouchPageIndex { get; set; }
    public string ThemeVariant { get; set; } = "Dark";

    public CloseButtonBehavior CloseButtonBehavior { get; set; } = CloseButtonBehavior.MinimizeToTray;
    public bool StartMinimizedToTray { get; set; }

    // Windows only: route keyboard macros through the Interception kernel driver
    // instead of SendInput, so injected keys reach raw-input apps (games / anti-cheat).
    // null = "auto" (active when the driver is installed); false = explicitly off.
    // Missing in older config.json simply stays null → auto behaviour (backward compatible).
    private bool? _interceptionEnabled;
    public bool? InterceptionEnabled
    {
        get => _interceptionEnabled;
        set { if (_interceptionEnabled == value) return; _interceptionEnabled = value; OnPropertyChanged(); }
    }

    // Visual flash overlay on touch press — useful especially on the Razer
    // (no LED ring on touch buttons) so the user gets visible feedback.
    private bool _touchFeedbackEnabled;
    public bool TouchFeedbackEnabled
    {
        get => _touchFeedbackEnabled;
        set { if (_touchFeedbackEnabled == value) return; _touchFeedbackEnabled = value; OnPropertyChanged(); }
    }

    private Avalonia.Media.Color _touchFeedbackColor = Avalonia.Media.Colors.White;
    public Avalonia.Media.Color TouchFeedbackColor
    {
        get => _touchFeedbackColor;
        set { if (_touchFeedbackColor == value) return; _touchFeedbackColor = value; OnPropertyChanged(); }
    }

    private double _touchFeedbackOpacity = 0.5;
    public double TouchFeedbackOpacity
    {
        get => _touchFeedbackOpacity;
        set { if (Math.Abs(_touchFeedbackOpacity - value) < 0.0001) return; _touchFeedbackOpacity = value; OnPropertyChanged(); }
    }

    // While a finger is down, ignore further TOUCH_START events until TOUCH_END.
    // Defends against the device emitting duplicate TOUCH_START at button
    // boundaries or when the finger slides across slots.
    private bool _touchSlidingPreventionEnabled = true;
    public bool TouchSlidingPreventionEnabled
    {
        get => _touchSlidingPreventionEnabled;
        set { if (_touchSlidingPreventionEnabled == value) return; _touchSlidingPreventionEnabled = value; OnPropertyChanged(); }
    }

    public SimpleButton[] SimpleButtons { get; set; }

    public ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; } = [];

    [JsonIgnore]
    public int CurrentRotaryPageIndex
    {
        get => _currentRotaryPageIndex;
        set
        {
            if (_currentRotaryPageIndex != value)
            {
                _currentRotaryPageIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentRotaryButtonPage));
            }
        }
    }

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonPage =>
        (RotaryButtonPages != null &&
         _currentRotaryPageIndex >= 0 &&
         _currentRotaryPageIndex < RotaryButtonPages.Count)
            ? RotaryButtonPages[_currentRotaryPageIndex]
            : null;

    public ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; } = [];

    [JsonIgnore]
    public int CurrentTouchPageIndex
    {
        get => _currentTouchPageIndex;
        set
        {
            if (_currentTouchPageIndex != value)
            {
                _currentTouchPageIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentTouchButtonPage));
            }
        }
    }

    [JsonIgnore]
    public TouchButtonPage CurrentTouchButtonPage =>
        (TouchButtonPages != null &&
         _currentTouchPageIndex >= 0 &&
         _currentTouchPageIndex < TouchButtonPages.Count)
            ? TouchButtonPages[_currentTouchPageIndex]
            : null;

    public int Brightness
    {
        get => _brightness;
        set
        {
            if (_brightness == value) return;
            _brightness = value;
            OnPropertyChanged();
        }
    }

    private SKBitmap _wallpaper;

    public SKBitmap Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (Equals(value, _wallpaper)) return;
            _wallpaper = value;
            OnPropertyChanged();
        }
    }

    // Briefly draws the page name on touch button 0 after switching pages.
    // Opt-in: many users find the 2s overlay distracting and prefer to keep
    // their layout visible.
    private bool _showPageNameOverlayEnabled;
    public bool ShowPageNameOverlayEnabled
    {
        get => _showPageNameOverlayEnabled;
        set { if (_showPageNameOverlayEnabled == value) return; _showPageNameOverlayEnabled = value; OnPropertyChanged(); }
    }

    private bool _hapticEnabled;
    public bool HapticEnabled
    {
        get => _hapticEnabled;
        set
        {
            if (_hapticEnabled == value) return;
            _hapticEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Ids of plugins the user has enabled. The v2→v3 migration seeds this from
    /// the former per-integration enable flags (see <c>PluginConfigMigrator</c>).
    /// </summary>
    public List<string> EnabledPlugins { get; set; } = [];

    // ObjectCreationHandling.Replace: Newtonsoft otherwise reuses the default
    // collection and appends deserialized items to it — so each save+load round
    // would duplicate every step.
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public ObservableCollection<HapticStep> HapticSteps { get; set; } = [new HapticStep()];

    private double _wallpaperOpacity;

    public double WallpaperOpacity
    {
        get => _wallpaperOpacity;
        set
        {
            if (!(Math.Abs(_wallpaperOpacity - value) > 0.0001)) return;
            _wallpaperOpacity = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}