using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class AboutViewModel() : DialogViewModelBase<DialogResult>
{
    public IRelayCommand OpenWebsiteCommand => field ??= Relay.Create(OpenWebsite);
    public IRelayCommand CloseCommand => field ??= Relay.Create(Close);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Viewmodel binding")]
    public string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var fullVersion = attribute?.InformationalVersion ?? "Unknown Version";
            var versionWithoutMetadata = fullVersion.Split('+')[0]; // Strips everything after the '+'
            return $"v{versionWithoutMetadata}";
        }
    }

    private static void OpenWebsite()
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