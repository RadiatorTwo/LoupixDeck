using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.Models.Argus;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.HwInfo;
using LoupixDeck.Services;
using LoupixDeck.Services.Argus;
using LoupixDeck.Services.HwInfo;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet.Communication;

namespace LoupixDeck.ViewModels;

public class SettingsViewModel : DialogViewModelBase<DialogResult>
{
    public LoupedeckConfig Config { get; }
    private readonly IObsController _obs;
    private readonly IDeviceService _deviceService;
    private readonly IElgatoController _elgatoController;
    private readonly IPageManager _pageManager;
    private readonly IArgusMonitorService _argusMonitor;
    private readonly IHwInfoService _hwInfo;
    private readonly IDialogService _dialogService;
    public ElgatoDevices ElgatoDevices { get; }

    public ICommand SaveObsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand AddHapticStepCommand { get; }
    public ICommand RemoveHapticStepCommand { get; }
    public ICommand ReconnectDeviceCommand { get; }
    public ICommand AddTouchPageCommand { get; }
    public ICommand RemoveTouchPageCommand { get; }
    public ICommand MoveTouchPageUpCommand { get; }
    public ICommand MoveTouchPageDownCommand { get; }
    public ICommand EditWallpaperCommand { get; }
    public ICommand EditPageCommandsCommand { get; }
    public ICommand AddRotaryPageCommand { get; }
    public ICommand RemoveRotaryPageCommand { get; }
    public ICommand MoveRotaryPageUpCommand { get; }
    public ICommand MoveRotaryPageDownCommand { get; }
    public ICommand RescanElgatoCommand { get; }
    public ICommand ToggleKeyLightCommand { get; }
    public ICommand TestCoolerControlCommand { get; }
    public ICommand OpenWebsiteCommand { get; }

    public ObservableCollection<VibrationPatternItem> VibrationPatterns => VibrationPatternCatalog.All;

    public bool IsWindows => OperatingSystem.IsWindows();

    public SettingsViewModel(LoupedeckConfig config,
        IObsController obs,
        IDeviceService deviceService,
        IElgatoController elgatoController,
        ElgatoDevices elgatoDevices,
        IPageManager pageManager,
        IArgusMonitorService argusMonitor,
        IHwInfoService hwInfo,
        IDialogService dialogService)
    {
        Config = config;
        _deviceService = deviceService;
        _elgatoController = elgatoController;
        ElgatoDevices = elgatoDevices ?? new ElgatoDevices();
        _pageManager = pageManager;
        _argusMonitor = argusMonitor;
        _hwInfo = hwInfo;
        _dialogService = dialogService;

        SaveObsCommand = new RelayCommand(SaveObs);
        TestConnectionCommand = new RelayCommand(TestConnection);

        NavigateCommand = new RelayCommand<SettingsView>(Navigate);
        AddHapticStepCommand = new RelayCommand(AddHapticStep);
        RemoveHapticStepCommand = new RelayCommand<HapticStep>(RemoveHapticStep);

        ReconnectDeviceCommand = new AsyncRelayCommand(ReconnectDevice);
        AddTouchPageCommand = new AsyncRelayCommand(() => _pageManager.AddTouchButtonPage());
        RemoveTouchPageCommand = new RelayCommand<TouchButtonPage>(
            p => _ = RemoveTouchPage(p),
            p => p != null && _pageManager.TouchButtonPages.Count > 1);
        MoveTouchPageUpCommand = new RelayCommand<TouchButtonPage>(
            p => MovePage(_pageManager.TouchButtonPages, p, -1),
            p => p != null && _pageManager.TouchButtonPages.IndexOf(p) > 0);
        MoveTouchPageDownCommand = new RelayCommand<TouchButtonPage>(
            p => MovePage(_pageManager.TouchButtonPages, p, +1),
            p => p != null && _pageManager.TouchButtonPages.IndexOf(p) < _pageManager.TouchButtonPages.Count - 1);
        EditWallpaperCommand = new RelayCommand<TouchButtonPage>(
            p => _ = EditWallpaper(p),
            p => p != null);
        EditPageCommandsCommand = new RelayCommand<object>(
            p => _ = EditPageCommands(p),
            p => p is TouchButtonPage or RotaryButtonPage);
        AddRotaryPageCommand = new RelayCommand(() => _pageManager.AddRotaryButtonPage());
        RemoveRotaryPageCommand = new RelayCommand<RotaryButtonPage>(
            RemoveRotaryPage,
            p => p != null && _pageManager.RotaryButtonPages.Count > 1);
        MoveRotaryPageUpCommand = new RelayCommand<RotaryButtonPage>(
            p => MovePage(_pageManager.RotaryButtonPages, p, -1),
            p => p != null && _pageManager.RotaryButtonPages.IndexOf(p) > 0);
        MoveRotaryPageDownCommand = new RelayCommand<RotaryButtonPage>(
            p => MovePage(_pageManager.RotaryButtonPages, p, +1),
            p => p != null && _pageManager.RotaryButtonPages.IndexOf(p) < _pageManager.RotaryButtonPages.Count - 1);

        // CollectionChanged can fire from a background thread (the parameterless
        // RelayCommand runs Execute via Task.Run). Marshal the CanExecute refresh
        // back to the UI thread so Avalonia's Button.IsEnabled update is safe.
        _pageManager.TouchButtonPages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshTouchPageCommands);
        _pageManager.RotaryButtonPages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshRotaryPageCommands);

        RescanElgatoCommand = new AsyncRelayCommand(RescanElgato);
        ToggleKeyLightCommand = new RelayCommand<KeyLight>(light => _ = ToggleKeyLight(light));
        TestCoolerControlCommand = new AsyncRelayCommand(TestCoolerControl);
        OpenWebsiteCommand = new RelayCommand(() =>
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

        Config.HapticSteps.CollectionChanged += OnHapticStepsChanged;

        CurrentView = SettingsView.General;

        ObsConfig = ObsConfig.LoadConfig();
        _obs = obs;
        _obs.Connected += ObsConnected;
        _obs.Disconnected += ObsDisconnected;
        ObsConnected_State = _obs == null ? false : false; // initial state unknown, treat as disconnected
        UpdateObsStatus();

        if (_argusMonitor != null)
        {
            _argusMonitor.SnapshotUpdated += OnArgusSnapshotUpdated;
            // Refresh on UI thread but lightweight — only reads the volatile snapshot field.
            Dispatcher.UIThread.Post(RefreshArgusSnapshot);
        }

        if (_hwInfo != null)
        {
            _hwInfo.SnapshotUpdated += OnHwInfoSnapshotUpdated;
            Dispatcher.UIThread.Post(RefreshHwInfoSnapshot);
        }

        // Device info call blocks on a serial round-trip — push it off the UI thread.
        _ = Task.Run(RefreshDeviceInfoAsync);

        Version = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}";
    }

    // ───────── General / Device ─────────

    public string DeviceName => _deviceService?.Device?.Type ?? "Device";

    private string _deviceVersion = "—";
    public string DeviceVersion { get => _deviceVersion; private set => SetProperty(ref _deviceVersion, value); }

    private string _deviceSerial = "—";
    public string DeviceSerial { get => _deviceSerial; private set => SetProperty(ref _deviceSerial, value); }

    private bool _deviceConnected;
    public bool DeviceConnected
    {
        get => _deviceConnected;
        private set
        {
            if (SetProperty(ref _deviceConnected, value))
            {
                OnPropertyChanged(nameof(DeviceStatusText));
            }
        }
    }

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
            serialHex = serial != null ? BitConverter.ToString(serial).Replace("-", "") : "—";
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

    // ───────── Pages ─────────

    public ObservableCollection<TouchButtonPage> TouchPages => _pageManager.TouchButtonPages;
    public ObservableCollection<RotaryButtonPage> RotaryPages => _pageManager.RotaryButtonPages;

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
        (MoveTouchPageUpCommand as RelayCommand<TouchButtonPage>)?.RaiseCanExecuteChanged();
        (MoveTouchPageDownCommand as RelayCommand<TouchButtonPage>)?.RaiseCanExecuteChanged();
        (RemoveTouchPageCommand as RelayCommand<TouchButtonPage>)?.RaiseCanExecuteChanged();
    }

    private void RefreshRotaryPageCommands()
    {
        (MoveRotaryPageUpCommand as RelayCommand<RotaryButtonPage>)?.RaiseCanExecuteChanged();
        (MoveRotaryPageDownCommand as RelayCommand<RotaryButtonPage>)?.RaiseCanExecuteChanged();
        (RemoveRotaryPageCommand as RelayCommand<RotaryButtonPage>)?.RaiseCanExecuteChanged();
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

    // ───────── OBS ─────────

    private bool _obsConnected;
    public bool ObsConnected_State
    {
        get => _obsConnected;
        private set
        {
            if (SetProperty(ref _obsConnected, value))
            {
                OnPropertyChanged(nameof(ObsStatusText));
                OnPropertyChanged(nameof(ObsStatusOk));
                OnPropertyChanged(nameof(ObsStatusNeutral));
            }
        }
    }
    public string ObsStatusText => ObsConnected_State ? "Connected" : (ObsConfig != null && !string.IsNullOrWhiteSpace(ObsConfig.Ip) ? "Disconnected" : "Not configured");
    public bool ObsStatusOk => ObsConnected_State;
    public bool ObsStatusNeutral => !ObsConnected_State;

    private void UpdateObsStatus()
    {
        // Lacking an explicit Connected-property on the controller; rely on event-driven state.
    }

    // ───────── Elgato ─────────

    /// <summary>
    /// Master switch for the Elgato integration. Enabling it kicks off a discovery
    /// probe immediately so Key Lights show up without waiting for the next app start.
    /// </summary>
    public bool ElgatoEnabled
    {
        get => Config.ElgatoEnabled;
        set
        {
            if (Config.ElgatoEnabled == value) return;
            Config.ElgatoEnabled = value;
            OnPropertyChanged();
            if (value)
                _ = _elgatoController.ProbeForElgatoDevices();
        }
    }

    private bool _isScanningElgato;
    public bool IsScanningElgato
    {
        get => _isScanningElgato;
        private set => SetProperty(ref _isScanningElgato, value);
    }

    public bool HasNoElgatoLights => (ElgatoDevices?.KeyLights?.Count ?? 0) == 0;

    private async Task RescanElgato()
    {
        if (IsScanningElgato) return;
        IsScanningElgato = true;
        try
        {
            await _elgatoController.ProbeForElgatoDevices();
        }
        finally
        {
            IsScanningElgato = false;
            OnPropertyChanged(nameof(ElgatoDevices));
            OnPropertyChanged(nameof(HasNoElgatoLights));
        }
    }

    private async Task ToggleKeyLight(KeyLight light)
    {
        if (light == null) return;
        try { await _elgatoController.Toggle(light); }
        catch { /* device may be offline */ }
    }

    // ───────── CoolerControl ─────────

    private string _coolerControlStatus = "Not tested";
    public string CoolerControlStatus { get => _coolerControlStatus; private set => SetProperty(ref _coolerControlStatus, value); }

    public enum CcState { Neutral, Ok, Warn, Error }
    private CcState _ccState = CcState.Neutral;
    public CcState CoolerControlState
    {
        get => _ccState;
        private set
        {
            if (SetProperty(ref _ccState, value))
            {
                OnPropertyChanged(nameof(CoolerControlIsOk));
                OnPropertyChanged(nameof(CoolerControlIsWarn));
                OnPropertyChanged(nameof(CoolerControlIsError));
                OnPropertyChanged(nameof(CoolerControlIsNeutral));
            }
        }
    }
    public bool CoolerControlIsOk      => _ccState == CcState.Ok;
    public bool CoolerControlIsWarn    => _ccState == CcState.Warn;
    public bool CoolerControlIsError   => _ccState == CcState.Error;
    public bool CoolerControlIsNeutral => _ccState == CcState.Neutral;

    public ObservableCollection<string> CoolerControlModes { get; } = [];

    private async Task TestCoolerControl()
    {
        CoolerControlStatus = "Testing…";
        CoolerControlState = CcState.Warn;
        CoolerControlModes.Clear();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(Config.CoolerControlUrl), Timeout = TimeSpan.FromSeconds(2) };
            var json = await client.GetStringAsync("modes");
            var arr = (JArray)JObject.Parse(json)["modes"];
            if (arr != null)
            {
                foreach (var mode in arr)
                {
                    var name = mode["name"]?.ToString() ?? mode["uid"]?.ToString() ?? "(unnamed)";
                    CoolerControlModes.Add(name);
                }
            }
            CoolerControlStatus = $"Connected — {CoolerControlModes.Count} modes";
            CoolerControlState = CcState.Ok;
        }
        catch (Exception ex)
        {
            CoolerControlStatus = $"Unreachable: {ex.Message}";
            CoolerControlState = CcState.Error;
        }
    }

    // ───────── Argus ─────────

    /// <summary>
    /// Master switch for the Argus Monitor integration. Toggling it starts/stops the
    /// shared-memory poll loop right away so the status reflects the change live.
    /// </summary>
    public bool ArgusMonitorEnabled
    {
        get => Config.ArgusMonitorEnabled;
        set
        {
            if (Config.ArgusMonitorEnabled == value) return;
            Config.ArgusMonitorEnabled = value;
            OnPropertyChanged();
            if (value)
                _argusMonitor?.Start();
            else
                _argusMonitor?.Stop();
            RefreshArgusSnapshot();
        }
    }

    public ObservableCollection<ArgusSensor> ArgusSensors { get; } = [];

    public string ArgusStatusText => (_argusMonitor?.IsAvailable ?? false) ? "Reading" : "Not running";
    public bool ArgusStatusOk => _argusMonitor?.IsAvailable ?? false;
    public bool ArgusStatusNeutral => !ArgusStatusOk;

    private void OnArgusSnapshotUpdated()
    {
        Dispatcher.UIThread.Post(RefreshArgusSnapshot);
    }

    private void RefreshArgusSnapshot()
    {
        OnPropertyChanged(nameof(ArgusStatusText));
        OnPropertyChanged(nameof(ArgusStatusOk));
        OnPropertyChanged(nameof(ArgusStatusNeutral));

        ArgusSensors.Clear();
        if (_argusMonitor?.Sensors == null) return;
        foreach (var s in _argusMonitor.Sensors.Take(60))
            ArgusSensors.Add(s);
    }

    // ───────── HWiNFO ─────────

    /// <summary>
    /// Master switch for the HWiNFO integration. Toggling it starts/stops the
    /// shared-memory poll loop right away so the status reflects the change live.
    /// </summary>
    public bool HwInfoEnabled
    {
        get => Config.HwInfoEnabled;
        set
        {
            if (Config.HwInfoEnabled == value) return;
            Config.HwInfoEnabled = value;
            OnPropertyChanged();
            if (value)
                _hwInfo?.Start();
            else
                _hwInfo?.Stop();
            RefreshHwInfoSnapshot();
        }
    }

    public ObservableCollection<HwInfoSensor> HwInfoSensors { get; } = [];

    public string HwInfoStatusText => (_hwInfo?.IsAvailable ?? false) ? "Reading" : "Not running";
    public bool HwInfoStatusOk => _hwInfo?.IsAvailable ?? false;
    public bool HwInfoStatusNeutral => !HwInfoStatusOk;
    public string HwInfoDiagnostics => _hwInfo?.Diagnostics ?? string.Empty;

    private void OnHwInfoSnapshotUpdated()
    {
        Dispatcher.UIThread.Post(RefreshHwInfoSnapshot);
    }

    private void RefreshHwInfoSnapshot()
    {
        OnPropertyChanged(nameof(HwInfoStatusText));
        OnPropertyChanged(nameof(HwInfoStatusOk));
        OnPropertyChanged(nameof(HwInfoStatusNeutral));
        OnPropertyChanged(nameof(HwInfoDiagnostics));

        HwInfoSensors.Clear();
        if (_hwInfo?.Sensors == null) return;
        foreach (var s in _hwInfo.Sensors.Take(60))
            HwInfoSensors.Add(s);
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

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
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

    public ObsConfig ObsConfig { get; }

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

    private void ObsConnected(object sender, EventArgs e)
    {
        ObsConnected_State = true;
        ConnectionResult = "Successfully connected";
        ConnectionTestVisible = true;
        TextColor = Colors.Green;
    }

    private void ObsDisconnected(object sender, ObsDisconnectionInfo e)
    {
        ObsConnected_State = false;
        ConnectionResult = $"Error: {e.WebsocketDisconnectionInfo.CloseStatusDescription}";
        ConnectionTestVisible = true;
        TextColor = Colors.Red;
    }

    private bool _connectionTestVisible;
    public bool ConnectionTestVisible
    {
        get => _connectionTestVisible;
        set => SetProperty(ref _connectionTestVisible, value);
    }

    private Color _textColor = Colors.Blue;
    public Color TextColor
    {
        get => _textColor;
        set => SetProperty(ref _textColor, value);
    }

    private string _connectionResult;
    public string ConnectionResult
    {
        get => _connectionResult;
        set => SetProperty(ref _connectionResult, value);
    }

    private void SaveObs() => ObsConfig.SaveConfig();
    private void TestConnection() => _obs.Connect(ObsConfig.Ip, ObsConfig.Port, ObsConfig.Password);

    private void Navigate(SettingsView settingsPage) => CurrentView = settingsPage;
}
