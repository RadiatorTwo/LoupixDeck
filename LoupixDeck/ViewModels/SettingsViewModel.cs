using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Services;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public partial class SettingsViewModel : DialogViewModelBase<DialogResult>
{
    public LoupedeckConfig Config { get; }
    private readonly IDeviceService _deviceService;
    private readonly IPageManager _pageManager;
    private readonly IDialogService _dialogService;
    private readonly IInterceptionService _interceptionService;
    private readonly IPluginReloadService _pluginReload;
    private readonly IPluginManager _pluginManager;
    private readonly IAutostartService _autostart;

    /// <summary>
    /// All discovered plugins — drives the Plugins settings page. Read live from the
    /// manager (its list is swapped on hot-reload), never cached, so the UI re-reads
    /// the current snapshot after an enable/disable/install/remove.
    /// </summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _pluginManager.Plugins;

    public IRelayCommand NavigateCommand => field ??= Relay.Create<SettingsView>(Navigate);
    public IRelayCommand AddHapticStepCommand => field ??= Relay.Create(AddHapticStep);
    public IRelayCommand RemoveHapticStepCommand => field ??= Relay.Create<HapticStep>(RemoveHapticStep);

    public IRelayCommand AddAppBindingCommand => field ??= Relay.Create(AddAppBinding);
    public IRelayCommand RemoveAppBindingCommand => field ??= Relay.Create<AppBindingRow>(RemoveAppBinding, static p => p != null);

    public IAsyncRelayCommand ReconnectDeviceCommand => field ??= Relay.Create(ReconnectDevice);
    public IAsyncRelayCommand AddTouchPageCommand => field ??= Relay.Create(() => _pageManager.AddTouchButtonPage());
    public IRelayCommand RemoveTouchPageCommand => field ??= Relay.Create<TouchButtonPage>(
        p => _ = RemoveTouchPage(p),
        p => p != null && _pageManager.TouchButtonPages.Count > 1);
    public IRelayCommand MoveTouchPageUpCommand => field ??= Relay.Create<TouchButtonPage>(
        p => MovePage(_pageManager.TouchButtonPages, p, -1),
        p => p != null && _pageManager.TouchButtonPages.IndexOf(p) > 0);
    public IRelayCommand MoveTouchPageDownCommand => field ??= Relay.Create<TouchButtonPage>(
        p => MovePage(_pageManager.TouchButtonPages, p, +1),
        p => p != null && _pageManager.TouchButtonPages.IndexOf(p) < _pageManager.TouchButtonPages.Count - 1);
    public IRelayCommand<TouchButtonPage> EditWallpaperCommand => field ??= Relay.Create<TouchButtonPage>(p => _ = EditWallpaper(p), p => p != null);

    public IRelayCommand EditPageCommandsCommand => field ??= Relay.Create<object>(
        p => _ = EditPageCommands(p),
        static p => p is TouchButtonPage or RotaryButtonPage);
    public IRelayCommand AddRotaryPageCommand => field ??= Relay.Create(() => _pageManager.AddRotaryButtonPage());
    public IRelayCommand RemoveRotaryPageCommand => field ??= Relay.Create<RotaryButtonPage>(
        RemoveRotaryPage, p => p != null && _pageManager.RotaryButtonPages.Count > 1);
    public IRelayCommand MoveRotaryPageUpCommand => field ??= Relay.Create<RotaryButtonPage>(
        p => MovePage(_pageManager.RotaryButtonPages, p, -1), p => p != null && _pageManager.RotaryButtonPages.IndexOf(p) > 0);
    public IRelayCommand MoveRotaryPageDownCommand => field ??= Relay.Create<RotaryButtonPage>(
        p => MovePage(_pageManager.RotaryButtonPages, p, +1), p => p != null && _pageManager.RotaryButtonPages.IndexOf(p) < _pageManager.RotaryButtonPages.Count - 1);

    // Side-specific rotary page management for devices with independent dial columns (Razer).
    public IRelayCommand AddLeftRotaryPageCommand => field ??= Relay.Create(() => _pageManager.AddRotaryButtonPage(RotarySide.Left));
    public IRelayCommand RemoveLeftRotaryPageCommand => field ??= Relay.Create<RotaryButtonPage>(p => RemoveSideRotaryPage(RotarySide.Left, p), p => p != null && LeftRotaryPages.Count > 1);
    public IRelayCommand AddRightRotaryPageCommand => field ??= Relay.Create(() => _pageManager.AddRotaryButtonPage(RotarySide.Right));
    public IRelayCommand RemoveRightRotaryPageCommand => field ??= Relay.Create<RotaryButtonPage>( p => RemoveSideRotaryPage(RotarySide.Right, p), p => p != null && RightRotaryPages.Count > 1);

    public IRelayCommand OpenWebsiteCommand => field ??= Relay.Create(() =>
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/RadiatorTwo/LoupixDeck",
                UseShellExecute = true
            });
        }
        catch { }
    });

    public IRelayCommand OpenPluginsFolderCommand => field ??= Relay.Create(OpenPluginsFolder);

    public IAsyncRelayCommand InstallInterceptionCommand => field ??= Relay.Create(InstallInterceptionAsync);
    public IAsyncRelayCommand UninstallInterceptionCommand => field ??= Relay.Create(UninstallInterceptionAsync);

    public IAsyncRelayCommand SelectScreensaverVideoCommand => field ??= Relay.Create(SelectScreensaverVideo);
    public IRelayCommand ClearScreensaverVideoCommand => field ??= Relay.Create(ClearScreensaverVideo);

    public ObservableCollection<VibrationPatternItem> VibrationPatterns => VibrationPatternCatalog.All;

    public bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Windows "run at startup" toggle. Backed directly by the HKCU Run entry
    /// (the same one the installer manages), so it needs no config field.</summary>
    public bool StartWithWindows
    {
        get => _autostart.IsEnabled();
        set { _autostart.SetEnabled(value); OnPropertyChanged(); }
    }

    /// <summary>
    /// App-focus page switching is available on Windows and on Linux (X11/XWayland).
    /// Gates the "App Switching" settings page — wider than <see cref="IsWindows"/>,
    /// so the editor stays hidden only on macOS / unsupported platforms.
    /// </summary>
    public bool IsAppSwitchingSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public SettingsViewModel(LoupedeckConfig config,
        IDeviceService deviceService,
        IPageManager pageManager,
        IDialogService dialogService,
        IPluginManager pluginManager,
        IPluginReloadService pluginReload,
        IInterceptionService interceptionService,
        IAutostartService autostart)
    {
        Config = config;
        _deviceService = deviceService;
        _pageManager = pageManager;
        _dialogService = dialogService;
        _interceptionService = interceptionService;
        _pluginReload = pluginReload;
        _pluginManager = pluginManager;
        _autostart = autostart;

        // Commands are created lazily on first access by their `field ??= Relay.Create(...)`
        // getters, so there is nothing to wire up here.

        // CollectionChanged can fire from a background thread (the parameterless
        // RelayCommand runs Execute via Task.Run). Marshal the CanExecute refresh
        // back to the UI thread so Avalonia's Button.IsEnabled update is safe.
        _pageManager.TouchButtonPages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                RefreshTouchPageCommands();
                SyncFallbackPageOptions();
            });
        _pageManager.RotaryButtonPages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                RefreshRotaryPageCommands();
                SyncRotaryPageOptions();
            });

        // Side-page lists drive the Remove buttons' CanExecute on side-strip devices.
        if (HasIndependentRotarySides)
        {
            LeftRotaryPages.CollectionChanged += (_, _) =>
                Dispatcher.UIThread.Post(RemoveLeftRotaryPageCommand.NotifyCanExecuteChanged);
            RightRotaryPages.CollectionChanged += (_, _) =>
                Dispatcher.UIThread.Post(RemoveRightRotaryPageCommand.NotifyCanExecuteChanged);
        }

        SyncRotaryPageOptions();
        SyncFallbackPageOptions();

        // Build the editor rows from the persisted rules. Rows and
        // Config.AppPageBindings are kept in sync manually in Add/RemoveAppBinding
        // (the only mutation paths), so no event subscription is needed here.
        foreach (var binding in Config.AppPageBindings)
            AppBindingRows.Add(new AppBindingRow(binding, TouchPages, RotaryPageOptions));

        Config.HapticSteps.CollectionChanged += OnHapticStepsChanged;

        CurrentView = SettingsView.General;

        // Device info call blocks on a serial round-trip — push it off the UI thread.
        _ = Task.Run(RefreshDeviceInfoAsync);

        // Probe the Interception driver state (DLL API) off the UI thread; drives the page.
        if (IsWindows)
            _ = Task.Run(RefreshInterceptionStatus);

        // Probe ffmpeg availability off the UI thread (the first probe can block briefly);
        // drives the screensaver "ffmpeg missing" hint.
        _ = Task.Run(() =>
        {
            var available = FfmpegDetector.IsAvailable();
            Dispatcher.UIThread.Post(() => FfmpegAvailable = available);
        });

        Version = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}";
    }

    // ───────── General / Device ─────────

    public string DeviceName => _deviceService?.Device?.Type ?? "Device";

    [ObservableProperty]
    public partial string DeviceVersion { get; private set; } = "—";
    [ObservableProperty]
    public partial string DeviceSerial { get; private set; } = "—";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceStatusText))]
    public partial bool DeviceConnected { get; private set; }

    public string DeviceStatusText => DeviceConnected ? "Connected" : "Disconnected";

    private async Task RefreshDeviceInfoAsync()
    {
        var dev = _deviceService?.Device;
        if (dev == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DeviceConnected = false;
                DeviceVersion = "—";
                DeviceSerial = "—";
            });
            return;
        }

        string version = "—";
        string serialHex = "—";
        var ok = false;
        try
        {
            // Blocks on serial round-trip (GetInfo does Send().GetAwaiter().GetResult()).
            // We are already off the UI thread because the caller used Task.Run.
            var (serial, ver) = dev.GetInfo();
            version = ver ?? "—";
            serialHex = serial != null ? Convert.ToHexString(serial) : "—";
            ok = true;
        }
        catch
        {
            // fall through with defaults
        }

        Dispatcher.UIThread.Post(() =>
        {
            DeviceVersion = version;
            DeviceSerial = serialHex;
            DeviceConnected = ok;
        });

        await Task.CompletedTask;
    }

    private async Task ReconnectDevice()
    {
        await Task.Run(() =>
        {
            try { _deviceService?.ReconnectDevice(); }
            catch { /* ignored */ }
        });
        await Task.Delay(500);
        await Task.Run(RefreshDeviceInfoAsync);
    }

    // ───────── Interception (Windows only) ─────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterceptionStatusText))]
    public partial bool InterceptionDriverInstalled { get; private set; }

    public string InterceptionStatusText => InterceptionDriverInstalled ? "Installed" : "Not installed";

    [ObservableProperty]
    public partial bool InterceptionBusy { get; private set; }
    [ObservableProperty]
    public partial string InterceptionStatusMessage { get; private set; } = string.Empty;

    /// <summary>
    /// The Use-Interception toggle. Maps the tri-state config (null = auto) onto a plain bool:
    /// auto is shown as enabled, so an installed driver is used by default.
    /// </summary>
    public bool InterceptionEnabled
    {
        get => Config.InterceptionEnabled ?? true;
        set
        {
            if ((Config.InterceptionEnabled ?? true) == value) return;
            Config.InterceptionEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Opens the user plugins folder in the OS file manager, creating it first
    /// if missing. This is the per-build config plugins dir
    /// (<c>~/.config/LoupixDeck[/debug]/plugins</c>), where users drop their own
    /// plugins. UseShellExecute=true routes a directory path through Explorer on
    /// Windows / xdg-open on Linux.
    /// </summary>
    private void OpenPluginsFolder()
    {
        try
        {
            var dir = System.IO.Path.Combine(FileDialogHelper.GetConfigDir(), "plugins");
            System.IO.Directory.CreateDirectory(dir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>Installs (or updates) a plugin from the given zip and loads it live.</summary>
    public Task<PluginActionResult> InstallPluginFromZipAsync(string zipPath) =>
        _pluginReload.InstallAsync(zipPath);

    /// <summary>Unloads and removes an installed (user) plugin live.</summary>
    public Task<PluginActionResult> RemovePluginAsync(LoadedPlugin plugin) =>
        _pluginReload.RemoveAsync(plugin);

    /// <summary>Loads a plugin live (no restart).</summary>
    public Task<PluginActionResult> EnablePluginAsync(string pluginId) =>
        _pluginReload.EnableAsync(pluginId);

    /// <summary>Unloads a plugin live (no restart).</summary>
    public Task<PluginActionResult> DisablePluginAsync(string pluginId) =>
        _pluginReload.DisableAsync(pluginId);

    private void RefreshInterceptionStatus()
    {
        var installed = _interceptionService.IsDriverInstalled();
        Dispatcher.UIThread.Post(() => InterceptionDriverInstalled = installed);
    }

    private async Task InstallInterceptionAsync()
    {
        InterceptionBusy = true;
        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => InterceptionStatusMessage = msg));
            await _interceptionService.DownloadAndInstallAsync(progress);
        }
        finally
        {
            InterceptionBusy = false;
            await Task.Run(RefreshInterceptionStatus);
        }
    }

    private async Task UninstallInterceptionAsync()
    {
        InterceptionBusy = true;
        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => InterceptionStatusMessage = msg));
            await _interceptionService.UninstallAsync(progress);
        }
        finally
        {
            InterceptionBusy = false;
            await Task.Run(RefreshInterceptionStatus);
        }
    }

    // ───────── Pages ─────────

    public ObservableCollection<TouchButtonPage> TouchPages => _pageManager.TouchButtonPages;
    public ObservableCollection<RotaryButtonPage> RotaryPages => _pageManager.RotaryButtonPages;

    /// <summary>True for devices that page their two dial columns independently (Razer):
    /// the rotary settings show separate left/right page lists instead of one.</summary>
    public bool HasIndependentRotarySides => _pageManager.HasIndependentRotarySides;
    public ObservableCollection<RotaryButtonPage> LeftRotaryPages => _pageManager.GetRotaryPages(RotarySide.Left);
    public ObservableCollection<RotaryButtonPage> RightRotaryPages => _pageManager.GetRotaryPages(RotarySide.Right);

    public ObservableCollection<int> TouchPageIndices
    {
        get
        {
            var c = new ObservableCollection<int>();
            if (TouchPages != null)
                for (var i = 0; i < TouchPages.Count; i++) c.Add(i);
            return c;
        }
    }

    // ───────── App Switching ─────────

    // The rule editor's touch-page selector binds directly to the live TouchPages
    // collection (a stable instance). A regenerated list would make the ComboBox
    // reset its SelectedIndex and — via the TwoWay binding — write that reset back
    // over the user's choice, so the selection appeared to "not save".

    /// <summary>Rotary page labels with a leading "(unchanged)" entry — maps to
    /// <c>AppPageBinding.RotarySelectionIndex</c> (0 = unchanged, n = page n). Kept
    /// as a single stable instance and synced in place (tail add/remove) so bound
    /// ComboBoxes never lose their selection.</summary>
    public ObservableCollection<string> RotaryPageOptions { get; } = new();

    /// <summary>Editor rows bound by the rule list. Kept in sync with
    /// <c>Config.AppPageBindings</c> in <see cref="AddAppBinding"/> /
    /// <see cref="RemoveAppBinding"/>.</summary>
    public ObservableCollection<AppBindingRow> AppBindingRows { get; } = new();
    public ObservableCollection<string> FallbackPageOptions { get; } = new();

    private void SyncFallbackPageOptions()
    {
        if (FallbackPageOptions.Count == 0) FallbackPageOptions.Add("(do nothing)");
        var want = (TouchPages?.Count ?? 0) + 1; // +1 for the "(do nothing)" entry
        while (FallbackPageOptions.Count > want) FallbackPageOptions.RemoveAt(FallbackPageOptions.Count - 1);
        while (FallbackPageOptions.Count < want) FallbackPageOptions.Add($"Page {FallbackPageOptions.Count}");
    }

    /// <summary>ComboBox helper for the no-match fallback: 0 = "(do nothing)" (null),
    /// n = touch page n-1. Maps to/from <c>Config.AppSwitchingFallbackTouchPageIndex</c>.</summary>
    public int FallbackSelectionIndex
    {
        get => Config.AppSwitchingFallbackTouchPageIndex is { } idx ? idx + 1 : 0;
        set
        {
            var newValue = value <= 0 ? (int?)null : value - 1;
            if (Config.AppSwitchingFallbackTouchPageIndex == newValue) return;
            Config.AppSwitchingFallbackTouchPageIndex = newValue;
            OnPropertyChanged();
        }
    }

    private void SyncRotaryPageOptions()
    {
        if (RotaryPageOptions.Count == 0) RotaryPageOptions.Add("(unchanged)");
        var want = (RotaryPages?.Count ?? 0) + 1; // +1 for the "(unchanged)" entry
        while (RotaryPageOptions.Count > want) RotaryPageOptions.RemoveAt(RotaryPageOptions.Count - 1);
        while (RotaryPageOptions.Count < want) RotaryPageOptions.Add($"Page {RotaryPageOptions.Count}");
    }

    private void AddAppBinding()
    {
        var binding = new AppPageBinding();
        Config.AppPageBindings.Add(binding);
        AppBindingRows.Add(new AppBindingRow(binding, TouchPages, RotaryPageOptions));
    }

    private void RemoveAppBinding(AppBindingRow row)
    {
        if (row == null) return;
        Config.AppPageBindings.Remove(row.Binding);
        AppBindingRows.Remove(row);
    }

    private async Task RemoveTouchPage(TouchButtonPage page)
    {
        if (page == null || TouchPages.Count <= 1) return;
        var idx = TouchPages.IndexOf(page);
        if (idx < 0) return;
        _pageManager.CurrentTouchPageIndex = idx;
        await _pageManager.DeleteTouchButtonPage();
    }

    private void RemoveRotaryPage(RotaryButtonPage page)
    {
        if (page == null || RotaryPages.Count <= 1) return;
        var idx = RotaryPages.IndexOf(page);
        if (idx < 0) return;
        _pageManager.CurrentRotaryPageIndex = idx;
        _pageManager.DeleteRotaryButtonPage();
    }

    private void RemoveSideRotaryPage(RotarySide side, RotaryButtonPage page)
    {
        var pages = _pageManager.GetRotaryPages(side);
        if (page == null || pages.Count <= 1) return;
        var idx = pages.IndexOf(page);
        if (idx < 0) return;
        // Point the side's current index at the page to delete, then delete it.
        _pageManager.ApplyRotaryPage(side, idx);
        _pageManager.DeleteRotaryButtonPage(side);
    }

    private void MovePage<T>(ObservableCollection<T> coll, T page, int delta)
    {
        if (page == null) return;
        var idx = coll.IndexOf(page);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= coll.Count) return;
        coll.Move(idx, target);

        // Renumber pages so PageName reflects the new order.
        var counter = 0;
        foreach (var item in coll)
        {
            counter++;
            switch (item)
            {
                case TouchButtonPage tp: tp.Page = counter; break;
                case RotaryButtonPage rp: rp.Page = counter; break;
            }
        }

        // Keep the current-page index pointing at the same page after the move.
        if (typeof(T) == typeof(TouchButtonPage))
        {
            _pageManager.CurrentTouchPageIndex = AdjustCurrentIndex(
                _pageManager.CurrentTouchPageIndex, idx, target);
        }
        else if (typeof(T) == typeof(RotaryButtonPage))
        {
            _pageManager.CurrentRotaryPageIndex = AdjustCurrentIndex(
                _pageManager.CurrentRotaryPageIndex, idx, target);
        }
    }

    private static int AdjustCurrentIndex(int current, int from, int to)
    {
        if (current == from) return to;
        if (from < current && to >= current) return current - 1;
        if (from > current && to <= current) return current + 1;
        return current;
    }

    private void RefreshTouchPageCommands()
    {
        MoveTouchPageUpCommand?.NotifyCanExecuteChanged();
        MoveTouchPageDownCommand?.NotifyCanExecuteChanged();
        RemoveTouchPageCommand?.NotifyCanExecuteChanged();
    }

    private void RefreshRotaryPageCommands()
    {
        MoveRotaryPageUpCommand?.NotifyCanExecuteChanged();
        MoveRotaryPageDownCommand?.NotifyCanExecuteChanged();
        RemoveRotaryPageCommand?.NotifyCanExecuteChanged();
    }

    // ───────── Screensaver ─────────

    /// <summary>Whether ffmpeg was found on PATH. Defaults to true (assume present) and is
    /// corrected by the async probe, so the "missing" hint only shows once we're sure.</summary>
    private bool _ffmpegAvailable = true;
    public bool FfmpegAvailable
    {
        get => _ffmpegAvailable;
        private set
        {
            if (SetProperty(ref _ffmpegAvailable, value))
                OnPropertyChanged(nameof(FfmpegMissing));
        }
    }

    /// <summary>Inverse of <see cref="FfmpegAvailable"/> for the settings hint visibility.</summary>
    public bool FfmpegMissing => !FfmpegAvailable;

    /// <summary>Display name of the selected screensaver clip, or a placeholder when none.
    /// Prefers the stored original file name; falls back to the (content-hash) asset file
    /// name for clips selected before the name was tracked.</summary>
    public string ScreensaverVideoDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Config.ScreensaverVideoName))
                return Config.ScreensaverVideoName;
            return string.IsNullOrWhiteSpace(Config.ScreensaverVideoPath)
                ? "(none)"
                : System.IO.Path.GetFileName(Config.ScreensaverVideoPath);
        }
    }

    private async Task SelectScreensaverVideo()
    {
        var path = await FileDialogHelper.OpenVideoDialog();
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        // Reference the chosen clip in place — do NOT copy it into the content-addressed
        // asset store. Screensaver clips can be large and are played by an external ffmpeg
        // process straight from disk, so a copy only wastes space and is confusing (it
        // looks like "the wrong file" is playing). Legacy configs that still hold an
        // "assets/screensavers/<hash>.<ext>" relative path keep working: ResolveAbsolute
        // handles both an absolute path and the old asset-relative form.
        Config.ScreensaverVideoPath = path;
        Config.ScreensaverVideoName = System.IO.Path.GetFileName(path);
        OnPropertyChanged(nameof(ScreensaverVideoDisplayName));
    }

    private void ClearScreensaverVideo()
    {
        Config.ScreensaverVideoPath = null;
        Config.ScreensaverVideoName = null;
        OnPropertyChanged(nameof(ScreensaverVideoDisplayName));
    }

    // ───────── Haptic ─────────

    public const int MaxHapticSteps = 2;

    private void OnHapticStepsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FirstHapticStep));
        OnPropertyChanged(nameof(SecondHapticStep));
        OnPropertyChanged(nameof(HasSecondHapticStep));
        OnPropertyChanged(nameof(CanAddHapticStep));
    }

    public HapticStep FirstHapticStep =>
        Config.HapticSteps.Count > 0 ? Config.HapticSteps[0] : null;

    public HapticStep SecondHapticStep =>
        Config.HapticSteps.Count > 1 ? Config.HapticSteps[1] : null;

    public bool HasSecondHapticStep => Config.HapticSteps.Count > 1;
    public bool CanAddHapticStep => Config.HapticSteps.Count < MaxHapticSteps;

    private void AddHapticStep()
    {
        if (Config.HapticSteps.Count >= MaxHapticSteps) return;
        Config.HapticSteps.Add(new HapticStep());
    }

    private void RemoveHapticStep(HapticStep step)
    {
        if (Config.HapticSteps.Count <= 1) return;
        Config.HapticSteps.RemoveAt(Config.HapticSteps.Count - 1);
    }

    // ───────── Theme ─────────

    public bool ThemeIsDark
    {
        get => string.Equals(Config.ThemeVariant, "Dark", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Dark"); }
    }
    public bool ThemeIsLight
    {
        get => string.Equals(Config.ThemeVariant, "Light", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("Light"); }
    }
    public bool ThemeIsSystem
    {
        get => string.Equals(Config.ThemeVariant, "System", StringComparison.OrdinalIgnoreCase);
        set { if (value) ApplyTheme("System"); }
    }

    private void ApplyTheme(string variant)
    {
        if (Config.ThemeVariant == variant) return;
        Config.ThemeVariant = variant;
        OnPropertyChanged(nameof(ThemeIsDark));
        OnPropertyChanged(nameof(ThemeIsLight));
        OnPropertyChanged(nameof(ThemeIsSystem));

        Application.Current?.RequestedThemeVariant = variant switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }

    // ───────── About ─────────

    public string Version { get; }

    // ───────── View navigation ─────────

    private SettingsView _currentView;
    public SettingsView CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    private async Task EditWallpaper(TouchButtonPage page)
    {
        if (page == null) return;
        await _dialogService.ShowDialogAsync<TouchPageWallpaperSettingsViewModel, DialogResult>(
            vm => vm.Initialize(page));
    }

    private async Task EditPageCommands(object page)
    {
        if (page is not TouchButtonPage && page is not RotaryButtonPage) return;
        await _dialogService.ShowDialogAsync<PageCommandsSettingsViewModel, DialogResult>(
            vm => vm.Initialize(page));
    }

    private void Navigate(SettingsView settingsPage) => CurrentView = settingsPage;
}
