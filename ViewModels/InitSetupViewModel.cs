using System.Collections.ObjectModel;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class DeviceItem(string name, string path, DeviceRegistry.DeviceInfo info, string serial)
{
    private string Name { get; } = name;
    public string Path { get; } = path;
    public DeviceRegistry.DeviceInfo Info { get; } = info;

    /// <summary>Normalized USB serial of this physical unit (null when none).</summary>
    public string Serial { get; } = serial;

    public override string ToString() => Name;
}

public partial class InitSetupViewModel : ViewModelBase
{
    public ObservableCollection<DeviceItem> SerialDevices { get; } = [];

#if DEBUG
    public ObservableCollection<int> BaudRates { get; } =
        [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1500000, 3000000, 480000000];
#else
    public ObservableCollection<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];
#endif

    [ObservableProperty] private DeviceItem _selectedDevice;

    // [ObservableProperty] private string _manualDevicePath;

    [ObservableProperty] private int _selectedBaudRate = 921600;

    [ObservableProperty] private string _connectionTestResult = string.Empty;

    public bool ConnectionWorking { get; set; }

    public InitSetupViewModel()
    {
    }

    public void Init()
    {
        var devices = SerialDeviceHelper.ListSerialUsbDevices();
        
        foreach (var device in devices)
        {
            var matchingDevice = DeviceRegistry.GetDeviceByVidPid(device.Vid, device.Pid);

            if (matchingDevice == null) continue;

            var resolved = new ResolvedDevice(matchingDevice, device.NormalizedSerial);

#if DEBUG
            // Hardware-less testing: env var LOUPIXDECK_FAKE_DEVICE=<slug> coerces
            // any detected supported device into that type so the multi-device
            // plumbing can be exercised against whatever's actually plugged in.
            resolved = FakeDeviceOverride.Apply(resolved);
#endif

            var name = $"{resolved.Info.Name} ({device.Vid}:{device.Pid})";
            SerialDevices.Add(new DeviceItem(name, device.DevNode, resolved.Info, resolved.Serial));
        }

        if (SerialDevices.Count == 0) return;
        
        SelectedDevice = SerialDevices[0];

        if (SerialDevices.Count == 1)
        {
            Confirm();
        }
    }

    // partial void OnSelectedDeviceChanged(DeviceItem value)
    // {
    //     ManualDevicePath = value.Path;
    // }

    [RelayCommand]
    private void TestConnection()
    {
        // ???
        if (string.IsNullOrWhiteSpace(SelectedDevice.Path))
        {
            ConnectionTestResult = "No device selected.";
            ConnectionWorking = false;
            return;
        }

        try
        {
            using var port = new SerialPort(SelectedDevice.Path, SelectedBaudRate);

            port.ReadTimeout = 1000;
            port.WriteTimeout = 1000;

            port.Open();

            if (port.IsOpen)
            {
                ConnectionTestResult = "Connection successful!";
                ConnectionWorking = true;
            }
            else
            {
                ConnectionTestResult = "Connection could not be opened.";
                ConnectionWorking = false;
            }

            port.Close();
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"Error: {ex.Message}";
            ConnectionWorking = false;
        }
    }

    [RelayCommand]
    public void Confirm()
    {
        TestConnection();

        if (ConnectionWorking)
        {
            CloseWindow?.Invoke();
        }
    }

    [RelayCommand]
    public void AbortCommand()
    {
        ConnectionWorking = false;
        CloseWindow?.Invoke();
    }

    public event Action CloseWindow;
}