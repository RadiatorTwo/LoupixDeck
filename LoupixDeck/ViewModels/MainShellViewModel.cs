using System.Collections.ObjectModel;
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
    public ObservableCollection<MainWindowViewModel> Devices { get; } = [];

    private MainWindowViewModel _selectedDevice;
    public MainWindowViewModel SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    /// <summary>Show the device tab strip only when more than one device is present,
    /// so the single-device window looks exactly as it did before phase 3.</summary>
    public bool HasMultipleDevices => Devices.Count > 1;

    public void Add(MainWindowViewModel device)
    {
        if (device == null) return;
        Devices.Add(device);
        _selectedDevice ??= device;
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
