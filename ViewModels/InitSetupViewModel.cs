using System.Collections.ObjectModel;
using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoupixDeck.ViewModels;

public partial class InitSetupViewModel : ViewModelBase
{
    public ObservableCollection<string> SerialDevices { get; } = [];
    public ObservableCollection<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200];

    [ObservableProperty]
    private string _selectedDevice;

    [ObservableProperty]
    private string _manualDevicePath;

    [ObservableProperty]
    private int _selectedBaudRate = 115200;
    
    [ObservableProperty]
    private string _connectionTestResult = string.Empty;
    
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
            ConnectionTestResult = "Kein Gerät ausgewählt.";
            return;
        }

        try
        {
            using var port = new SerialPort(ManualDevicePath, SelectedBaudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            port.Open();

            if (port.IsOpen)
            {
                // Optional: Testbefehl senden, z. B. "ping"
                // port.WriteLine("ping");

                ConnectionTestResult = "Verbindung erfolgreich!";
            }
            else
            {
                ConnectionTestResult = "Verbindung konnte nicht geöffnet werden.";
            }

            port.Close();
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"Fehler: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        // Optional: Validierung oder Zwischenspeichern
        CloseWindow?.Invoke();
    }

    public event Action CloseWindow;
}