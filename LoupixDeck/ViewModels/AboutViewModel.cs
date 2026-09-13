using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Updates;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public sealed partial class AboutViewModel(IUpdateService updateService, IDialogService dialogService)
    : DialogViewModelBase<DialogResult>
{
    public IRelayCommand OpenWebsiteCommand => field ??= Relay.Create(OpenWebsite);
    public IRelayCommand CloseCommand => field ??= Relay.Create(Close);

    /// <summary>Works whether or not the automatic check is turned on.</summary>
    public IAsyncRelayCommand CheckForUpdatesCommand => field ??= Relay.Create(CheckForUpdatesAsync);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Viewmodel binding")]
    public string Version => $"v{AppVersion.Text ?? "?"}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateStatus))]
    public partial string UpdateStatus { get; set; }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(UpdateStatus);

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = Loc.Tr("About_CheckingForUpdates");
        UpdateCheckResult result = await updateService.CheckAsync(manual: true);

        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                UpdateStatus = Loc.Tr("About_UpToDate");
                break;
            case UpdateCheckStatus.UpdateAvailable:
                // Close first: the update dialog is modal to the main window, not stacked on About.
                UpdateStatus = null;
                Close();
                Dispatcher.UIThread.Post(() => _ = dialogService.ShowDialogAsync<UpdateDialogViewModel, DialogResult>(
                    vm => vm.Initialize(result.Update)));
                break;
            default:
                UpdateStatus = Loc.Tr("About_UpdateCheckFailed", result.Error);
                break;
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
