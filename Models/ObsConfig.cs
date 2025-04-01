using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

public class ObsConfig : INotifyPropertyChanged
{
    private string _ip;
    private int _port;
    private string _password;

    public string Ip
    {
        get => _ip;
        set
        {
            if (value == _ip) return;
            _ip = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Url));
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (value == _port) return;
            _port = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Url));
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (value == _password) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    public string Url => $"ws://{Ip}:{Port}";

    public static ObsConfig LoadConfig()
    {
        var filePath = Utils.FileDialogHelper.GetConfigPath("obsconfig.json");
        if (!File.Exists(filePath))
        {
            return new ObsConfig
            {
                Ip = "127.0.0.1",
                Port = 4455,
                Password = ""
            };
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<ObsConfig>(json);
            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");

            return new ObsConfig
            {
                Ip = "127.0.0.1",
                Port = 4455,
                Password = ""
            };
        }
    }

    public void SaveConfig()
    {
        try
        {
            var filePath = Utils.FileDialogHelper.GetConfigPath("obsconfig.json");
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
            Console.WriteLine("Configuration saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}