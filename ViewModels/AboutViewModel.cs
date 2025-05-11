using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class AboutViewModel : DialogViewModelBase<DialogResult>
{
    public ICommand OpenWebsiteCommand { get; }
    public ICommand CloseCommand { get; }

    public string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var fullVersion = attribute?.InformationalVersion ?? "Unknown Version";
            var versionWithoutMetadata = fullVersion.Split('+')[0]; // Entfernt alles nach dem '+'
            return $"v{versionWithoutMetadata}";
        }
    }

    public AboutViewModel()
    {
        OpenWebsiteCommand = new RelayCommand(OpenWebsite);
        CloseCommand = new RelayCommand(Close);
    }

    private void OpenWebsite()
    {
        const string url = "https://github.com/RadiatorTwo/LoupixDeck";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Handle exception if needed
            Console.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }

    private void Close()
    {
        CloseWindow?.Invoke();
    }

    public event Action CloseWindow;
}