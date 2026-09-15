using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Services.Updates;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Top-level shell that hosts one <see cref="MainWindowViewModel"/> per running
/// device (issue #116 phase 3). The single MainWindow binds to this; a device
/// tab strip selects which device's layout the DeviceLayoutHost shows, and the
/// hamburger menu / tray target <see cref="SelectedDevice"/>.
///
/// Not a DI service — it aggregates view models built from several device child
/// providers, so App constructs it and adds each device's VM.
/// </summary>
public sealed class MainShellViewModel : ViewModelBase
{
    private readonly LoupixDeck.Services.IDialogService _dialogService;
    private readonly IUpdateService _updateService;
    private readonly IUpdateNotifier _updateNotifier;

    /// <param name="dialogService">Taken from the primary device's container, which exists as
    /// soon as a device is configured — whether or not it is currently reachable. Only the
    /// About and update dialogs are opened through it here, and those need no device.</param>
    /// <param name="updateService">The app-wide update check (root container); drives the update hint.</param>
    /// <param name="updateNotifier">OS notification used while the window sits in the tray.</param>
    /// <param name="pluginStore">The app-wide plugin store (root container); drives the plugin update hint.</param>
    public MainShellViewModel(LoupixDeck.Services.IDialogService dialogService = null,
        IUpdateService updateService = null, IUpdateNotifier updateNotifier = null,
        IPluginStoreService pluginStore = null)
    {
        _dialogService = dialogService;
        _updateService = updateService;
        _updateNotifier = updateNotifier;
        _pluginStore = pluginStore;
        AboutMenuCommand = new AsyncRelayCommand(ShowAbout);
        ShowUpdateCommand = new AsyncRelayCommand(ShowUpdate);
        ShowPluginUpdatesCommand = new AsyncRelayCommand(ShowPluginUpdates);
        SelectDeviceCommand = new RelayCommand<string>(SelectDevice, CanSelectDevice);

        if (_updateService != null)
        {
            _updateService.PropertyChanged += OnUpdateServicePropertyChanged;
            _updateService.UpdateFound += update => UpdateFound?.Invoke(update);
        }

        if (_pluginStore != null)
            _pluginStore.PropertyChanged += OnPluginStorePropertyChanged;
    }

    // ───────── Plugin update hint (issue #234) ─────────

    private readonly IPluginStoreService _pluginStore;

    /// <summary>The app update strip takes the row while both are pending; plugins follow once it is gone.</summary>
    public bool HasPluginUpdates => !HasUpdate && _pluginStore?.AvailableUpdates.Count > 0;

    public string PluginUpdateHintText => _pluginStore?.AvailableUpdates is { Count: > 0 } updates
        ? updates.Count == 1
            ? Loc.Tr("PluginStore_UpdateHintOne", updates[0].Entry.DisplayName, updates[0].Available.Version)
            : Loc.Tr("PluginStore_UpdateHintMany", updates.Count)
        : string.Empty;

    public IAsyncRelayCommand ShowPluginUpdatesCommand { get; }

    private void OnPluginStorePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IPluginStoreService.AvailableUpdates)) return;

        OnPropertyChanged(nameof(HasPluginUpdates));
        OnPropertyChanged(nameof(PluginUpdateHintText));
    }

    /// <summary><c>ui-settings.json</c> key: ids of missing plugins the user declined to install, comma-separated.</summary>
    private const string DeclinedMissingPluginsKey = "PluginStoreDeclinedMissing";

    /// <summary>
    /// After startup: when the configs use commands of plugins that are not installed, offers to open the
    /// store for them. Asked once per plugin; declining is remembered. Never throws.
    /// </summary>
    public async Task PromptForMissingPluginsAsync()
    {
        if (_pluginStore == null) return;

        try
        {
            IReadOnlyList<PluginCommandOwner> missing = await _pluginStore.FindMissingPluginsAsync();
            HashSet<string> declined = new(
                (Utils.UiSettingsStore.GetString(DeclinedMissingPluginsKey) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            List<PluginCommandOwner> toAsk = missing.Where(m => !declined.Contains(m.PluginId)).ToList();
            if (toAsk.Count == 0 || SelectedDevice == null) return;

            Console.WriteLine($"[PluginStore] Configs use missing plugins: {string.Join(", ", toAsk.Select(m => m.PluginId))}.");

            string names = string.Join(", ", toAsk.Select(m => m.DisplayName));
            bool open = await Utils.ConfirmDialogHelper.AskYesNoAsync(Utils.WindowHelper.GetMainWindow(),
                Loc.Tr("PluginStore_MissingTitle"), Loc.Tr("PluginStore_MissingMessage", names));

            if (open)
            {
                if (SelectedDevice != null)
                    await SelectedDevice.OpenPluginStoreAsync(toAsk[0].PluginId);
                return;
            }

            declined.UnionWith(toAsk.Select(m => m.PluginId));
            Utils.UiSettingsStore.Set(DeclinedMissingPluginsKey, string.Join(",", declined));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginStore] Missing plugin check failed: {ex.Message}");
        }
    }

    private Task ShowPluginUpdates()
    {
        IReadOnlyList<PluginStoreItem> updates = _pluginStore?.AvailableUpdates;
        string highlighted = updates is { Count: 1 } ? updates[0].Entry.Id : null;
        return SelectedDevice?.OpenPluginStoreAsync(highlighted) ?? Task.CompletedTask;
    }

    // ───────── Update hint (issue #233) ─────────

    /// <summary>Raised on the UI thread when a new, not skipped release is found; the window turns it
    /// into an OS notification while it sits in the tray.</summary>
    public event Action<UpdateInfo> UpdateFound;

    public bool HasUpdate => _updateService?.AvailableUpdate != null;

    public string UpdateHintText => _updateService?.AvailableUpdate is { } update
        ? Loc.Tr("Update_Available", update.Latest.Tag, $"v{update.InstalledVersion}")
        : string.Empty;

    public IAsyncRelayCommand ShowUpdateCommand { get; }

    /// <summary>Announces <paramref name="update"/> as an OS notification.</summary>
    /// <param name="windowHandle">Native handle of the main window (used on Windows).</param>
    public void NotifyUpdate(UpdateInfo update, IntPtr windowHandle)
    {
        _updateNotifier?.Show(Loc.Tr("Update_NotificationTitle"),
            Loc.Tr("Update_Available", update.Latest.Tag, $"v{update.InstalledVersion}"), windowHandle);
    }

    private void OnUpdateServicePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IUpdateService.AvailableUpdate)) return;

        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateHintText));
        OnPropertyChanged(nameof(HasPluginUpdates));
    }

    private async Task ShowUpdate()
    {
        UpdateInfo update = _updateService?.AvailableUpdate;
        if (_dialogService == null || update == null) return;

        await _dialogService.ShowDialogAsync<UpdateDialogViewModel, LoupixDeck.Models.DialogResult>(
            vm => vm.Initialize(update));
    }

    public ObservableCollection<MainWindowViewModel> Devices { get; } = [];

    private MainWindowViewModel _selectedDevice;
    public MainWindowViewModel SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
                OnPropertyChanged(nameof(HasDevice));
        }
    }

    /// <summary>Selects the running device with the given scope key, e.g. a companion's master from
    /// the header hint. Not executable while that device is not running.</summary>
    public RelayCommand<string> SelectDeviceCommand { get; }

    private void SelectDevice(string scopeKey)
    {
        if (FindDevice(scopeKey) is { } device)
            SelectedDevice = device;
    }

    private bool CanSelectDevice(string scopeKey) => FindDevice(scopeKey) != null;

    private MainWindowViewModel FindDevice(string scopeKey) =>
        string.IsNullOrWhiteSpace(scopeKey)
            ? null
            : Devices.FirstOrDefault(d => string.Equals(d.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Show the device tab strip only when more than one device is present,
    /// so the single-device window looks exactly as it did before phase 3.</summary>
    public bool HasMultipleDevices => Devices.Count > 1;

    /// <summary>False while every device is unplugged. The window then hides the
    /// context switcher (its Profile/Workspace selectors have nothing to show) and
    /// puts an explicit "no device connected" state in the device area instead.</summary>
    public bool HasDevice => _selectedDevice != null;

    /// <summary>About shows the app version and a link and reaches no hardware, so it lives on
    /// the shell next to Quit and stays usable while no device is connected.</summary>
    public IAsyncRelayCommand AboutMenuCommand { get; }

    private async Task ShowAbout()
    {
        if (_dialogService == null) return;
        await _dialogService.ShowDialogAsync<AboutViewModel, LoupixDeck.Models.DialogResult>();
    }

    /// <summary>Quit has to work with no device connected too, so the shell owns it rather
    /// than delegating to a device's view model.</summary>
    public IRelayCommand QuitApplicationCommand { get; } = new RelayCommand(() =>
    {
        if (Utils.WindowHelper.GetMainWindow() is Views.MainWindow window)
        {
            window.QuitApplication();
            return;
        }

        Environment.Exit(0);
    });

    public void Add(MainWindowViewModel device)
    {
        if (device == null) return;
        Devices.Add(device);
        // Go through the property so the change is announced: after the last device
        // was unplugged SelectedDevice is null, and writing the backing field here
        // would make the caller's later "SelectedDevice = vm" a silent no-op — the
        // window would never rebuild its device layout (blank shell on re-plug).
        if (_selectedDevice == null)
            SelectedDevice = device;
        OnPropertyChanged(nameof(HasMultipleDevices));
        SelectDeviceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drop a device's VM (hot-unplug). If it was the selected one, fall back
    /// to the first remaining device (or null when none are left).</summary>
    public void Remove(MainWindowViewModel device)
    {
        if (device == null) return;
        var wasSelected = ReferenceEquals(_selectedDevice, device);
        Devices.Remove(device);
        if (wasSelected)
            SelectedDevice = Devices.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleDevices));
        SelectDeviceCommand.NotifyCanExecuteChanged();
    }
}
