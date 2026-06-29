using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// This data model holds all configuration settings,
/// which are loaded and saved via JSON.
/// </summary>
/// <remarks>
/// Change notification is provided by the MVVM Community Toolkit: deriving from
/// <see cref="ObservableObject"/> implements <see cref="System.ComponentModel.INotifyPropertyChanged"/>,
/// and <c>[ObservableProperty]</c> source-generates the implementations of each property.
/// Dependent computed properties are refreshed via
/// <c>[NotifyPropertyChangedFor]</c>; the generated <c>On…Changing</c>/<c>On…Changed</c> hooks keep the
/// page collections' <see cref="INotifyCollectionChanged"/> subscriptions in sync.
/// </remarks>
public partial class LoupedeckConfig : ObservableObject
{
    public LoupedeckConfig()
    {
        // A note on initializing from the constructor vs property setters:
        // *Usually* setters are called to, you know, set the property (whether by us or a deserializer)
        // But in fact an inline `... { get; set; } = new();` goes straight to the field
        // bypassing any extra event-wiring - thus, this constructor enforces the execution of MvvmToolkit's
        // generated setters (and calling of our handlers)
        RotaryButtonPages = new();
        LeftRotaryButtonPages = new();
        RightRotaryButtonPages = new();
        TouchButtonPages = new();

        // AppPageBindings and HapticSteps don't need
        // to be assigned here, because (as of writing) there is no
        // additional wiring that needs to be done upon assignment
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
    [ObservableProperty]
    public partial bool? InterceptionEnabled { get; set; }

    // Visual flash overlay on touch press — useful especially on the Razer
    // (no LED ring on touch buttons) so the user gets visible feedback.
    [ObservableProperty]
    public partial bool TouchFeedbackEnabled { get; set; }

    [ObservableProperty]
    public partial Avalonia.Media.Color TouchFeedbackColor { get; set; } = Avalonia.Media.Colors.White;

    // Hand-written: an epsilon guard suppresses redundant notifications for the tiny
    // float deltas a slider can emit, which the generated exact-equality check wouldn't.
    public double TouchFeedbackOpacity
    {
        get; set => SetProperty(ref field, value, EpsilonComparer.Default);
    } = 0.5;

    // While a finger is down, ignore further TOUCH_START events until TOUCH_END.
    // Defends against the device emitting duplicate TOUCH_START at button
    // boundaries or when the finger slides across slots.
    [ObservableProperty]
    public partial bool TouchSlidingPreventionEnabled { get; set; } = true;

    // ───────── Screensaver (issue #120) ─────────
    // Full-display animated screensaver: after the device has been idle for
    // ScreensaverIdleTimeoutSeconds, the configured video/GIF (decoded via ffmpeg)
    // plays across the whole display until the user touches the hardware again.
    // All fields below are additive with safe defaults, so a config saved before
    // this feature loads unchanged (the screensaver is simply off by default).

    [ObservableProperty]
    public partial bool ScreensaverEnabled { get; set; }

    [ObservableProperty]
    public partial int ScreensaverIdleTimeoutSeconds { get; set; } = 300;

    [ObservableProperty]
    public partial int ScreensaverFps { get; set; } = 30;

    // Relative asset path (e.g. "assets/screensavers/<hash>.mp4") of the source clip,
    // or null when none is chosen. Resolved through the asset folder at playback time.
    [ObservableProperty]
    public partial string ScreensaverVideoPath { get; set; }

    // Original file name of the chosen clip (display only). The asset itself is stored
    // content-addressed (hash filename), so this keeps a human-readable label in settings.
    [ObservableProperty]
    public partial string ScreensaverVideoName { get; set; }

    [ObservableProperty]
    public partial bool ScreensaverLoop { get; set; } = true;

    public SimpleButton[] SimpleButtons { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; }

    partial void OnRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => RotaryButtonPages?.CollectionChanged -= OnRotaryPagesChanged;
    partial void OnRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => RotaryButtonPages?.CollectionChanged += OnRotaryPagesChanged;
    private void OnRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Deleting a non-last page leaves CurrentRotaryPageIndex unchanged, so the
        // index-driven [NotifyPropertyChangedFor] never fires even though the page
        // at that index is now a different object. Re-evaluate the projection here.
        OnPropertyChanged(nameof(RotaryPageLabel));
        OnPropertyChanged(nameof(CurrentRotaryButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(RotaryPageLabel))]
    public partial int CurrentRotaryPageIndex { get; set; } = -1;

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonPage =>
        (RotaryButtonPages != null &&
         CurrentRotaryPageIndex >= 0 &&
         CurrentRotaryPageIndex < RotaryButtonPages.Count)
            ? RotaryButtonPages[CurrentRotaryPageIndex]
            : null;

    /// <summary>"current / total" label for the rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RotaryPageLabel =>
        RotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentRotaryPageIndex + 1, 1, RotaryButtonPages.Count)} / {RotaryButtonPages.Count}"
            : "0 / 0";

    // --- Independent left/right rotary pages (devices with side strips) -------
    // Devices with side strips (Razer Stream Controller) page each dial column on
    // its own: LeftRotaryButtonPages / RightRotaryButtonPages each hold that side's
    // knobs (3 on the Razer, re-indexed 0-based per side). Devices without side
    // strips (Live S) leave these empty and keep using RotaryButtonPages (Both).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftRotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> LeftRotaryButtonPages { get; set; }

    partial void OnLeftRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => LeftRotaryButtonPages?.CollectionChanged -= OnLeftRotaryPagesChanged;
    partial void OnLeftRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => LeftRotaryButtonPages?.CollectionChanged += OnLeftRotaryPagesChanged;
    private void OnLeftRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LeftRotaryPageLabel));
        OnPropertyChanged(nameof(CurrentLeftRotaryButtonPage));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightRotaryPageLabel))]
    public partial ObservableCollection<RotaryButtonPage> RightRotaryButtonPages { get; set; }

    partial void OnRightRotaryButtonPagesChanging(ObservableCollection<RotaryButtonPage> value) => RightRotaryButtonPages?.CollectionChanged -= OnRightRotaryPagesChanged;
    partial void OnRightRotaryButtonPagesChanged(ObservableCollection<RotaryButtonPage> value) => RightRotaryButtonPages?.CollectionChanged += OnRightRotaryPagesChanged;
    private void OnRightRotaryPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RightRotaryPageLabel));
        OnPropertyChanged(nameof(CurrentRightRotaryButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentLeftRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(LeftRotaryPageLabel))]
    public partial int CurrentLeftRotaryPageIndex { get; set; } = -1;

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentRightRotaryButtonPage))]
    [NotifyPropertyChangedFor(nameof(RightRotaryPageLabel))]
    public partial int CurrentRightRotaryPageIndex { get; set; } = -1;

    [JsonIgnore]
    public RotaryButtonPage CurrentLeftRotaryButtonPage =>
        (LeftRotaryButtonPages != null &&
         CurrentLeftRotaryPageIndex >= 0 &&
         CurrentLeftRotaryPageIndex < LeftRotaryButtonPages.Count)
            ? LeftRotaryButtonPages[CurrentLeftRotaryPageIndex]
            : null;

    [JsonIgnore]
    public RotaryButtonPage CurrentRightRotaryButtonPage =>
        (RightRotaryButtonPages != null &&
         CurrentRightRotaryPageIndex >= 0 &&
         CurrentRightRotaryPageIndex < RightRotaryButtonPages.Count)
            ? RightRotaryButtonPages[CurrentRightRotaryPageIndex]
            : null;

    /// <summary>"current / total" label for the left rotary pager (1-based).</summary>
    [JsonIgnore]
    public string LeftRotaryPageLabel =>
        LeftRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentLeftRotaryPageIndex + 1, 1, LeftRotaryButtonPages.Count)} / {LeftRotaryButtonPages.Count}"
            : "0 / 0";

    /// <summary>"current / total" label for the right rotary pager (1-based).</summary>
    [JsonIgnore]
    public string RightRotaryPageLabel =>
        RightRotaryButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentRightRotaryPageIndex + 1, 1, RightRotaryButtonPages.Count)} / {RightRotaryButtonPages.Count}"
            : "0 / 0";

    // Strip rendering mode is per rotary page (see RotaryButtonPage.StripMode), not
    // global per side — each page on a column can independently be Segmented/FreeDraw.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TouchPageLabel))]
    public partial ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; }

    partial void OnTouchButtonPagesChanging(ObservableCollection<TouchButtonPage> value) => TouchButtonPages?.CollectionChanged -= OnTouchPagesChanged;
    partial void OnTouchButtonPagesChanged(ObservableCollection<TouchButtonPage> value) => TouchButtonPages?.CollectionChanged += OnTouchPagesChanged;
    private void OnTouchPagesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TouchPageLabel));
        OnPropertyChanged(nameof(CurrentTouchButtonPage));
    }

    [ObservableProperty]
    [JsonIgnore]
    [NotifyPropertyChangedFor(nameof(CurrentTouchButtonPage))]
    [NotifyPropertyChangedFor(nameof(TouchPageLabel))]
    public partial int CurrentTouchPageIndex { get; set; } = -1;

    [JsonIgnore]
    public TouchButtonPage CurrentTouchButtonPage =>
        (TouchButtonPages != null &&
         CurrentTouchPageIndex >= 0 &&
         CurrentTouchPageIndex < TouchButtonPages.Count)
            ? TouchButtonPages[CurrentTouchPageIndex]
            : null;

    /// <summary>"current / total" label for the touch pager (1-based).</summary>
    [JsonIgnore]
    public string TouchPageLabel =>
        TouchButtonPages is { Count: > 0 }
            ? $"{Math.Clamp(CurrentTouchPageIndex + 1, 1, TouchButtonPages.Count)} / {TouchButtonPages.Count}"
            : "0 / 0";

    [ObservableProperty]
    public partial int Brightness { get; set; } = 100;

    // Briefly draws the page name on touch button 0 after switching pages.
    // Opt-in: many users find the 2s overlay distracting and prefer to keep
    // their layout visible.
    [ObservableProperty]
    public partial bool ShowPageNameOverlayEnabled { get; set; }

    [ObservableProperty]
    public partial bool HapticEnabled { get; set; }

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
    [ObservableProperty]
    public partial bool AppSwitchingEnabled { get; set; }

    // Ordered rule list — first match wins. ObjectCreationHandling.Replace for the
    // same reason as HapticSteps (avoid Newtonsoft appending to the default instance).
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public ObservableCollection<AppPageBinding> AppPageBindings { get; set; } = [];

    // Touch page to switch to when no rule matches. null = do nothing on no-match.
    public int? AppSwitchingFallbackTouchPageIndex { get; set; }
}
