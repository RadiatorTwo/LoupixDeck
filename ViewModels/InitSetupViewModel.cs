using System.Collections.ObjectModel;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public partial class InitSetupViewModel : ViewModelBase
{
    public ObservableCollection<string> SerialDevices { get; } = [];
    public ObservableCollection<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];

    [ObservableProperty] private string _selectedDevice;

    [ObservableProperty] private string _manualDevicePath;

    [ObservableProperty] private int _selectedBaudRate = 921600;

    [ObservableProperty] private string _connectionTestResult = string.Empty;

    public bool ConnectionWorking { get; set; }

    public InitSetupViewModel()
    {
        var ports = SerialPort.GetPortNames();
        foreach (var port in ports.OrderBy(p => p))
            SerialDevices.Add(port);
    }

    partial void OnSelectedDeviceChanged(string value)
    {
        ManualDevicePath = value;
    }

    [RelayCommand]
    private void TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ManualDevicePath))
        {
            ConnectionTestResult = "No device selected.";
            ConnectionWorking = false;
            return;
        }

        try
        {
            using var port = new SerialPort(ManualDevicePath, SelectedBaudRate);

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