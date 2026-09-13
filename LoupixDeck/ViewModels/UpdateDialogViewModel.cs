using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.Updates;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>Release notes of one version, as shown in the update dialog.</summary>
public sealed record ReleaseNotesItem(string Heading, string Notes);

/// <summary>
/// "A new version is available": the release notes of every version between the installed and the
/// latest one, and Update now / Later / Skip this version (issue #233).
/// </summary>
public sealed partial class UpdateDialogViewModel(IUpdateService updateService, IUpdateInstaller installer)
    : DialogViewModelBase<DialogResult>
{
    private UpdateInfo _update;
    private CancellationTokenSource _download;
    private bool _installerStarted;

    public string Headline { get; private set; } = string.Empty;

    public IReadOnlyList<ReleaseNotesItem> Releases { get; private set; } = [];

    /// <summary>Installations that cannot update themselves get the release page instead.</summary>
    public string UpdateNowText => installer.Mode == UpdateInstallMode.ReleasePage
        ? Loc.Tr("Update_OpenReleasePage")
        : Loc.Tr("Update_UpdateNow");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand), nameof(SkipCommand))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool ShowProgress => IsBusy;

    public IAsyncRelayCommand UpdateNowCommand => field ??= Relay.Create(UpdateNowAsync, () => !IsBusy && !_installerStarted);

    public IRelayCommand LaterCommand => field ??= Relay.Create(() =>
    {
        CancelDownload();
        Cancel();
        CloseWindow?.Invoke();
    });

    public IRelayCommand SkipCommand => field ??= Relay.Create(() =>
    {
        updateService.SkipVersion(_update);
        Cancel();
        CloseWindow?.Invoke();
    }, () => !IsBusy);

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    public void CancelDownload()
    {
        _download?.Cancel();
    }

    public void Initialize(UpdateInfo update)
    {
        _update = update;
        Headline = Loc.Tr("Update_Available", update.Latest.Tag, $"v{update.InstalledVersion}");
        Releases = update.Releases
            .Select(r => new ReleaseNotesItem(
                string.IsNullOrWhiteSpace(r.Name) || r.Name == r.Tag ? r.Tag : $"{r.Tag} - {r.Name}",
                string.IsNullOrWhiteSpace(r.Notes) ? Loc.Tr("Update_NoReleaseNotes") : r.Notes.Trim()))
            .ToList();
    }

    private async Task UpdateNowAsync()
    {
        if (_update == null)
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        StatusText = installer.Mode == UpdateInstallMode.ReleasePage ? null : Loc.Tr("Update_Downloading");
        _download = new CancellationTokenSource();

        try
        {
            Progress<double> progress = new(value => Progress = value * 100);
            UpdateInstallResult result = await installer.InstallAsync(_update.Latest, progress, _download.Token);

            switch (result.Outcome)
            {
                case UpdateInstallOutcome.InstallerStarted:
                    // The installer closes the app; starting it a second time would only race it.
                    _installerStarted = true;
                    StatusText = installer.Mode == UpdateInstallMode.LinuxScript
                        ? Loc.Tr("Update_ContinuesInTerminal")
                        : Loc.Tr("Update_InstallerStarted");
                    break;
                case UpdateInstallOutcome.ReleasePageOpened:
                    Confirm(new DialogResult(true));
                    CloseWindow?.Invoke();
                    break;
                default:
                    StatusText = Loc.Tr("Update_Failed", result.Error);
                    break;
            }
        }
        finally
        {
            _download.Dispose();
            _download = null;
            IsBusy = false;
        }
    }
}
