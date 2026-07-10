using System.Collections.ObjectModel;
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
        Profiles = new();

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
    /// v7 introduced stateful buttons: each button's appearance moved into a default
    /// <c>ButtonState</c> (touch buttons: layers/background/haptic; LED buttons: <c>ButtonColor</c>
    /// → <c>LedColor</c>); the button now projects the active state. See <c>ButtonStatesMigrator</c>.
    /// v8 introduced profiles and workspaces (issue #132): the touch/rotary page collections and
    /// <c>StartupTouchPageIndex</c> moved off the config root into a <c>Home</c> <see cref="Workspace"/>
    /// inside a <c>Default</c> <see cref="Profile"/>; the root now holds <see cref="Profiles"/> plus the
    /// active/startup profile ids, and the former page properties forward to the active workspace.
    /// See <c>ProfilesWorkspacesMigrator</c>.
    /// </summary>
    public const int CurrentVersion = 8;

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

    // ───────── Profiles / Workspaces (issue #132) ─────────
    // The touch/rotary page collections and their active-page projections used to live
    // directly here. Since v8 they live inside the active workspace (Profiles → Workspaces →
    // Pages). The former root-level properties are kept below as forwarding facades so every
    // existing binding and caller (PageManager, device layouts, SettingsViewModel) keeps
    // working unchanged; they resolve against ActiveWorkspace.

    [ObservableProperty]
    public partial ObservableCollection<Profile> Profiles { get; set; }

    /// <summary>Id of the currently active profile. Persisted; resolved via <see cref="ActiveProfile"/>.</summary>
    [ObservableProperty]
    public partial Guid ActiveProfileId { get; set; }

    /// <summary>Id of the profile activated at launch (issue #132 replaced the former single
    /// device-wide startup page). Each profile then opens on its home workspace.</summary>
    public Guid StartupProfileId { get; set; }

    /// <summary>Runtime-only id of the active workspace within the active profile. Not persisted —
    /// a launch always starts on the startup profile's home workspace.</summary>
    [ObservableProperty]
    [JsonIgnore]
    public partial Guid ActiveWorkspaceId { get; set; }

    /// <summary>The active profile resolved from <see cref="ActiveProfileId"/>, falling back to the
    /// first profile. Null only before <see cref="EnsureDefaultProfile"/> has run.</summary>
    [JsonIgnore]
    public Profile ActiveProfile =>
        Profiles?.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles?.FirstOrDefault();

    /// <summary>The active workspace resolved from <see cref="ActiveWorkspaceId"/> within the active
    /// profile, falling back to the profile's home workspace.</summary>
    [JsonIgnore]
    public Workspace ActiveWorkspace =>
        ActiveProfile is { } p
            ? (p.Workspaces?.FirstOrDefault(w => w.Id == ActiveWorkspaceId) ?? p.HomeWorkspace)
            : null;

    // The workspace whose PropertyChanged we currently forward. Kept so we can unhook it on switch.
    private Workspace _boundWorkspace;

    /// <summary>
    /// Ensures the config always has at least a Default profile with a Home workspace, and that the
    /// active/startup ids resolve. Idempotent — a migrated or already-populated config is left as is
    /// (only empty ids are healed). Also (re)binds the active-workspace forwarding. Call once after
    /// load / construction (see the device DI factory).
    /// </summary>
    public void EnsureDefaultProfile()
    {
        Profiles ??= new();

        if (Profiles.Count == 0)
        {
            var workspace = new Workspace { Name = "Home" };
            var profile = new Profile { Name = "Default", HomeWorkspaceId = workspace.Id };
            profile.Workspaces.Add(workspace);
            Profiles.Add(profile);
        }

        // Resolve the configured startup profile (heal if it points nowhere).
        if (Profiles.All(p => p.Id != StartupProfileId))
            StartupProfileId = Profiles[0].Id;

        // A launch always starts on the configured startup profile, opened at its home workspace
        // (issue #132 — "configurable startup profile + Home", replacing the former single startup
        // page). EnsureDefaultProfile runs once at load, so this is the launch entry point.
        ActiveProfileId = StartupProfileId;
        ActiveWorkspaceId = ActiveProfile?.HomeWorkspace?.Id ?? Guid.Empty;

        RebindActiveWorkspace();
    }

    partial void OnActiveProfileIdChanged(Guid value) => RebindActiveWorkspace();
    partial void OnActiveWorkspaceIdChanged(Guid value) => RebindActiveWorkspace();

    /// <summary>
    /// Re-points the facade at the current <see cref="ActiveWorkspace"/>: unhooks the previously
    /// forwarded workspace, subscribes to the new one, and raises change notifications for every
    /// forwarded property so all bindings refresh when the active profile/workspace switches.
    /// </summary>
    public void RebindActiveWorkspace()
    {
        var workspace = ActiveWorkspace;
        if (!ReferenceEquals(workspace, _boundWorkspace))
        {
            if (_boundWorkspace != null)
                _boundWorkspace.PropertyChanged -= OnActiveWorkspacePropertyChanged;
            _boundWorkspace = workspace;
            if (_boundWorkspace != null)
                _boundWorkspace.PropertyChanged += OnActiveWorkspacePropertyChanged;
        }

        RaiseActiveWorkspaceProjections();
    }

    // The active workspace projects page state under the same property names as the facade below,
    // so forwarding the notification 1:1 refreshes the corresponding facade binding.
    private void OnActiveWorkspacePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged(e.PropertyName);

    private void RaiseActiveWorkspaceProjections()
    {
        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(TouchButtonPages));
        OnPropertyChanged(nameof(RotaryButtonPages));
        OnPropertyChanged(nameof(LeftRotaryButtonPages));
        OnPropertyChanged(nameof(RightRotaryButtonPages));
        OnPropertyChanged(nameof(CurrentTouchButtonPage));
        OnPropertyChanged(nameof(CurrentRotaryButtonPage));
        OnPropertyChanged(nameof(CurrentLeftRotaryButtonPage));
        OnPropertyChanged(nameof(CurrentRightRotaryButtonPage));
        OnPropertyChanged(nameof(CurrentTouchPageIndex));
        OnPropertyChanged(nameof(CurrentRotaryPageIndex));
        OnPropertyChanged(nameof(CurrentLeftRotaryPageIndex));
        OnPropertyChanged(nameof(CurrentRightRotaryPageIndex));
        OnPropertyChanged(nameof(TouchPageLabel));
        OnPropertyChanged(nameof(RotaryPageLabel));
        OnPropertyChanged(nameof(LeftRotaryPageLabel));
        OnPropertyChanged(nameof(RightRotaryPageLabel));
        OnPropertyChanged(nameof(StartupTouchPageIndex));
    }

    // ───────── Active-workspace facade (forwards to ActiveWorkspace) ─────────
    // All [JsonIgnore]: the data now serializes under Profiles/Workspaces, not at the root.

    [JsonIgnore]
    public int StartupTouchPageIndex
    {
        get => ActiveWorkspace?.StartupTouchPageIndex ?? 0;
        set { if (ActiveWorkspace is { } ws) ws.StartupTouchPageIndex = value; }
    }

    [JsonIgnore]
    public ObservableCollection<RotaryButtonPage> RotaryButtonPages => ActiveWorkspace?.RotaryButtonPages;

    [JsonIgnore]
    public ObservableCollection<RotaryButtonPage> LeftRotaryButtonPages => ActiveWorkspace?.LeftRotaryButtonPages;

    [JsonIgnore]
    public ObservableCollection<RotaryButtonPage> RightRotaryButtonPages => ActiveWorkspace?.RightRotaryButtonPages;

    [JsonIgnore]
    public ObservableCollection<TouchButtonPage> TouchButtonPages => ActiveWorkspace?.TouchButtonPages;

    [JsonIgnore]
    public int CurrentRotaryPageIndex
    {
        get => ActiveWorkspace?.CurrentRotaryPageIndex ?? -1;
        set { if (ActiveWorkspace is { } ws) ws.CurrentRotaryPageIndex = value; }
    }

    [JsonIgnore]
    public int CurrentLeftRotaryPageIndex
    {
        get => ActiveWorkspace?.CurrentLeftRotaryPageIndex ?? -1;
        set { if (ActiveWorkspace is { } ws) ws.CurrentLeftRotaryPageIndex = value; }
    }

    [JsonIgnore]
    public int CurrentRightRotaryPageIndex
    {
        get => ActiveWorkspace?.CurrentRightRotaryPageIndex ?? -1;
        set { if (ActiveWorkspace is { } ws) ws.CurrentRightRotaryPageIndex = value; }
    }

    [JsonIgnore]
    public int CurrentTouchPageIndex
    {
        get => ActiveWorkspace?.CurrentTouchPageIndex ?? -1;
        set { if (ActiveWorkspace is { } ws) ws.CurrentTouchPageIndex = value; }
    }

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonPage => ActiveWorkspace?.CurrentRotaryButtonPage;

    [JsonIgnore]
    public RotaryButtonPage CurrentLeftRotaryButtonPage => ActiveWorkspace?.CurrentLeftRotaryButtonPage;

    [JsonIgnore]
    public RotaryButtonPage CurrentRightRotaryButtonPage => ActiveWorkspace?.CurrentRightRotaryButtonPage;

    [JsonIgnore]
    public TouchButtonPage CurrentTouchButtonPage => ActiveWorkspace?.CurrentTouchButtonPage;

    [JsonIgnore]
    public string RotaryPageLabel => ActiveWorkspace?.RotaryPageLabel ?? "0 / 0";

    [JsonIgnore]
    public string LeftRotaryPageLabel => ActiveWorkspace?.LeftRotaryPageLabel ?? "0 / 0";

    [JsonIgnore]
    public string RightRotaryPageLabel => ActiveWorkspace?.RightRotaryPageLabel ?? "0 / 0";

    [JsonIgnore]
    public string TouchPageLabel => ActiveWorkspace?.TouchPageLabel ?? "0 / 0";

    [ObservableProperty]
    public partial int Brightness { get; set; } = 100;

    // Ordered dithering when downsampling to the panel's RGB565 framebuffer. Trades the
    // hard steps of a gradient for a fine pattern the eye averages into intermediate tones.
    // Matters most on the Razer Stream Controller, whose panel discards the low bit of red
    // and so shows only 16 red levels. On by default; the pattern is a matter of taste, and
    // a user who prefers hard steps to fine grain can switch it off. Absent from older
    // config files, where the default applies.
    [ObservableProperty]
    public partial bool DitheringEnabled { get; set; } = true;

    // Briefly draws the page name on touch button 0 after switching pages.
    // Opt-in: many users find the 2s overlay distracting and prefer to keep
    // their layout visible.
    [ObservableProperty]
    public partial bool ShowPageNameOverlayEnabled { get; set; }

    // Slide transition when a rotary (side-strip) page changes. On by default; a user who
    // finds the slide distracting can switch to instant paging. Additive optional field —
    // absent from an old config it defaults to true (unchanged behaviour after the feature).
    [ObservableProperty]
    public partial bool RotaryPageTransitionAnimationEnabled { get; set; } = true;

    // Slide transition when a touch page changes. Same opt-out semantics as the rotary flag.
    [ObservableProperty]
    public partial bool TouchPageTransitionAnimationEnabled { get; set; } = true;

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

    // --- Context rules (issue #132) -------------------------------------------
    // Profile/workspace-aware successor to AppPageBindings. Additive and optional: a config saved
    // before #132 has no ContextRules, so it loads with an empty list and the context engine folds
    // the legacy AppPageBindings in at runtime (see AppSwitchingService). ObjectCreationHandling
    // .Replace for the same reason as HapticSteps/AppPageBindings.
    [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
    public ObservableCollection<ContextRule> ContextRules { get; set; } = [];

    // Profile to activate when leaving all rule-matched apps. null = restore whichever profile was
    // active before a rule first took over (previous-profile behaviour).
    public Guid? FallbackProfileId { get; set; }
}
