using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <param name="dialogService">Taken from the primary device's container, which exists as
    /// soon as a device is configured — whether or not it is currently reachable. Only the
    /// About dialog is opened through it here, and that one needs no device.</param>
    public MainShellViewModel(LoupixDeck.Services.IDialogService dialogService = null)
    {
        _dialogService = dialogService;
        AboutMenuCommand = new AsyncRelayCommand(ShowAbout);
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
    }
}
