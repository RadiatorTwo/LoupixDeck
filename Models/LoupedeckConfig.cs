using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private int _currentLeftRotaryPageIndex = -1;
    private int _currentRightRotaryPageIndex = -1;
    private int _currentTouchPageIndex = -1;

    private int _brightness = 100;

    public LoupedeckConfig()
    {
        // Newtonsoft populates the field-initialized collections in place (no
        // ObjectCreationHandling.Replace), so the property setters never fire on
        // load — subscribe here to keep the page-count labels in sync.
        _rotaryButtonPages.CollectionChanged += OnRotaryPagesChanged;
        _leftRotaryButtonPages.CollectionChanged += OnLeftRotaryPagesChanged;
        _rightRotaryButtonPages.CollectionChanged += OnRightRotaryPagesChanged;
        _touchButtonPages.CollectionChanged += OnTouchPagesChanged;
    }

    /// <summary>
    /// Schema version of the persisted config. <see cref="ConfigService"/> runs
    /// the migration chain for older versions (see <c>Services/Migrations</c>).
    /// v3 introduced the plugin system: the integration-specific fields were
    /// removed and the per-integration enable flags became <see cref="EnabledPlugins"/>.
    /// v4 split the single <see cref="RotaryButtonPages"/> list into independent
    /// <see cref="LeftRotaryButtonPages"/> / <see cref="RightRotaryButtonPages"/> sets
    /// for devices with side strips (Razer); see <c>RotaryPageSideSplitMigrator</c>.
    /// v5 moved page wallpapers from inline Base64 into the asset folder (relative path
    /// + scaling parameters on each page); see <c>WallpaperAssetMigrator</c>.
    /// v6 nested the flat main-wallpaper fields into a <c>MainWallpaper</c> slot and added
    /// optional left/right side-display wallpapers; see <c>WallpaperSlotMigrator</c>.
    /// </summary>
    public const int CurrentVersion = 6;

    public int Version { get; set; } = CurrentVersion;

    public string DevicePort { get; set; }
    public int DeviceBaudrate { get; set; }

    /// <summary>USB vendor ID of the device this config belongs to (hex, e.g. "2ec2").</summary>
    public string DeviceVid { get; set; }

    /// <summary>USB product ID of the device this config belongs to (hex, e.g. "0006").</summary>
    public string DevicePid { get; set; }

    /// <summary>
    /// Normalized USB iSerialNumber of the physical device this config belongs to
    /// (platform-uniform; see <c>SerialNormalizer</c>). Null for devices without a
    /// real serial. Used to scope the config file and to re-detect the right port
    /// when two identical devices are present. Additive — absent in older configs.
    /// </summary>
    public string DeviceSerial { get; set; }

    public int StartupTouchPageIndex { get; set; }
    public string ThemeVariant { get; set; } = "Dark";

    public CloseButtonBehavior CloseButtonBehavior { get; set; } = CloseButtonBehavior.MinimizeToTray;
    public bool StartMinimizedToTray { get; set; }

    // Windows only: route keyboard and mouse macros through the Interception kernel driver
    // instead of SendInput, so injected input reaches raw-input apps (games / anti-cheat).
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

    // ───────── Screensaver (issue #120) ─────────
    // Full-display animated screensaver: after the device has been idle for
    // ScreensaverIdleTimeoutSeconds, the configured video/GIF (decoded via ffmpeg)
    // plays across the whole display until the user touches the hardware again.
    // All fields below are additive with safe defaults, so a config saved before
    // this feature loads unchanged (the screensaver is simply off by default).

    private bool _screensaverEnabled;
    public bool ScreensaverEnabled
    {
        get => _screensaverEnabled;
        set { if (_screensaverEnabled == value) return; _screensaverEnabled = value; OnPropertyChanged(); }
    }

    private int _screensaverIdleTimeoutSeconds = 300;
    public int ScreensaverIdleTimeoutSeconds
    {
        get => _screensaverIdleTimeoutSeconds;
        set { if (_screensaverIdleTimeoutSeconds == value) return; _screensaverIdleTimeoutSeconds = value; OnPropertyChanged(); }
    }

    private int _screensaverFps = 30;
    public int ScreensaverFps
    {
        get => _screensaverFps;
        set { if (_screensaverFps == value) return; _screensaverFps = value; OnPropertyChanged(); }
    }

    // Relative asset path (e.g. "assets/screensavers/<hash>.mp4") of the source clip,
    // or null when none is chosen. Resolved through the asset folder at playback time.
    private string _screensaverVideoPath;
    public string ScreensaverVideoPath
    {
        get => _screensaverVideoPath;
        set { if (_screensaverVideoPath == value) return; _screensaverVideoPath = value; OnPropertyChanged(); }
    }

    private bool _screensaverLoop = true;
    public bool ScreensaverLoop
    {
        get => _screensaverLoop;
        set { if (_screensaverLoop == value) return; _screensaverLoop = value; OnPropertyChanged(); }
    }

    public SimpleButton[] SimpleButtons { get; set; }

    private ObservableCollection<RotaryButtonPage> _rotaryButtonPages = [];
    public ObservableCollection<RotaryButtonPage> RotaryButtonPages
    {
        get => _rotaryButtonPages;
        set
        {
            if (ReferenceEquals(_rotaryButtonPages, value)) return;
            if (_rotaryButtonPages != null)
                _rotaryButtonPages.CollectionChanged -= OnRotaryPagesChanged;
            _rotaryButtonPages = value;
            if (_rotaryButtonPages != null)
                _rotaryButtonPages.CollectionChanged += OnRotaryPagesChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotaryPageLabel));
        }
    }

    private void OnRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(RotaryPageLabel));

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
                OnPropertyChanged(nameof(RotaryPageLabel));
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

    /// <summary>"current / total" label for the rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RotaryPageLabel =>
        RotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(_currentRotaryPageIndex + 1, 1, RotaryButtonPages.Count)} / {RotaryButtonPages.Count}"
            : "0 / 0";

    // --- Independent left/right rotary pages (devices with side strips) -------
    // Devices with side strips (Razer Stream Controller) page each dial column on
    // its own: LeftRotaryButtonPages / RightRotaryButtonPages each hold that side's
    // knobs (3 on the Razer, re-indexed 0-based per side). Devices without side
    // strips (Live S) leave these empty and keep using RotaryButtonPages (Both).

    private ObservableCollection<RotaryButtonPage> _leftRotaryButtonPages = [];
    public ObservableCollection<RotaryButtonPage> LeftRotaryButtonPages
    {
        get => _leftRotaryButtonPages;
        set
        {
            if (ReferenceEquals(_leftRotaryButtonPages, value)) return;
            if (_leftRotaryButtonPages != null)
                _leftRotaryButtonPages.CollectionChanged -= OnLeftRotaryPagesChanged;
            _leftRotaryButtonPages = value;
            if (_leftRotaryButtonPages != null)
                _leftRotaryButtonPages.CollectionChanged += OnLeftRotaryPagesChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LeftRotaryPageLabel));
        }
    }

    private void OnLeftRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(LeftRotaryPageLabel));

    private ObservableCollection<RotaryButtonPage> _rightRotaryButtonPages = [];
    public ObservableCollection<RotaryButtonPage> RightRotaryButtonPages
    {
        get => _rightRotaryButtonPages;
        set
        {
            if (ReferenceEquals(_rightRotaryButtonPages, value)) return;
            if (_rightRotaryButtonPages != null)
                _rightRotaryButtonPages.CollectionChanged -= OnRightRotaryPagesChanged;
            _rightRotaryButtonPages = value;
            if (_rightRotaryButtonPages != null)
                _rightRotaryButtonPages.CollectionChanged += OnRightRotaryPagesChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RightRotaryPageLabel));
        }
    }

    private void OnRightRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(RightRotaryPageLabel));

    [JsonIgnore]
    public int CurrentLeftRotaryPageIndex
    {
        get => _currentLeftRotaryPageIndex;
        set
        {
            if (_currentLeftRotaryPageIndex == value) return;
            _currentLeftRotaryPageIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentLeftRotaryButtonPage));
            OnPropertyChanged(nameof(LeftRotaryPageLabel));
        }
    }

    [JsonIgnore]
    public int CurrentRightRotaryPageIndex
    {
        get => _currentRightRotaryPageIndex;
        set
        {
            if (_currentRightRotaryPageIndex == value) return;
            _currentRightRotaryPageIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentRightRotaryButtonPage));
            OnPropertyChanged(nameof(RightRotaryPageLabel));
        }
    }

    [JsonIgnore]
    public RotaryButtonPage CurrentLeftRotaryButtonPage =>
        (LeftRotaryButtonPages != null &&
         _currentLeftRotaryPageIndex >= 0 &&
         _currentLeftRotaryPageIndex < LeftRotaryButtonPages.Count)
            ? LeftRotaryButtonPages[_currentLeftRotaryPageIndex]
            : null;

    [JsonIgnore]
    public RotaryButtonPage CurrentRightRotaryButtonPage =>
        (RightRotaryButtonPages != null &&
         _currentRightRotaryPageIndex >= 0 &&
         _currentRightRotaryPageIndex < RightRotaryButtonPages.Count)
            ? RightRotaryButtonPages[_currentRightRotaryPageIndex]
            : null;

    /// <summary>"current / total" label for the left rotary pager (1-based).</summary>
    [JsonIgnore]
    public string LeftRotaryPageLabel =>
        LeftRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(_currentLeftRotaryPageIndex + 1, 1, LeftRotaryButtonPages.Count)} / {LeftRotaryButtonPages.Count}"
            : "0 / 0";

    /// <summary>"current / total" label for the right rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RightRotaryPageLabel =>
        RightRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(_currentRightRotaryPageIndex + 1, 1, RightRotaryButtonPages.Count)} / {RightRotaryButtonPages.Count}"
            : "0 / 0";

    // Strip rendering mode is per rotary page (see RotaryButtonPage.StripMode), not
    // global per side — each page on a column can independently be Segmented/FreeDraw.

    private ObservableCollection<TouchButtonPage> _touchButtonPages = [];
    public ObservableCollection<TouchButtonPage> TouchButtonPages
    {
        get => _touchButtonPages;
        set
        {
            if (ReferenceEquals(_touchButtonPages, value)) return;
            if (_touchButtonPages != null)
                _touchButtonPages.CollectionChanged -= OnTouchPagesChanged;
            _touchButtonPages = value;
            if (_touchButtonPages != null)
                _touchButtonPages.CollectionChanged += OnTouchPagesChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TouchPageLabel));
        }
    }

    private void OnTouchPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(TouchPageLabel));

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
                OnPropertyChanged(nameof(TouchPageLabel));
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

    /// <summary>"current / total" label for the touch pager (1-based).</summary>
    [JsonIgnore]
    public string TouchPageLabel =>
        TouchButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(_currentTouchPageIndex + 1, 1, TouchButtonPages.Count)} / {TouchButtonPages.Count}"
            : "0 / 0";

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

    // --- App-focus page switching (Feature 2) ---------------------------------
    // Master toggle for the foreground-window → page mapping.
    private bool _appSwitchingEnabled;
    public bool AppSwitchingEnabled
    {
        get => _appSwitchingEnabled;
        set { if (_appSwitchingEnabled == value) return; _appSwitchingEnabled = value; OnPropertyChanged(); }
    }

    // Ordered rule list — first match wins. ObjectCreationHandling.Replace for the
    // same reason as HapticSteps (avoid Newtonsoft appending to the default instance).
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public ObservableCollection<AppPageBinding> AppPageBindings { get; set; } = [];

    // Touch page to switch to when no rule matches. null = do nothing on no-match.
    public int? AppSwitchingFallbackTouchPageIndex { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}