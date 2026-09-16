using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Release notes of the plugin version the store is about to install or update to (issue #234), with
/// Install / Cancel. Nothing is downloaded before the user confirms. The notes themselves are read from
/// GitHub while the dialog is already open, so a slow or failing lookup never delays the decision.
/// </summary>
public sealed partial class PluginReleaseNotesViewModel(IPluginStoreService store)
    : DialogViewModelBase<DialogResult>
{
    private PluginReleaseCandidate _candidate;

    public string Headline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    public string ConfirmText { get; private set; } = string.Empty;

    /// <summary>
    /// False when the dialog is only showing the notes rather than asking whether to install.
    /// The store's per-tile "Release notes" link opens it that way: Cancel becomes the only
    /// button, and closing it installs nothing.
    /// </summary>
    public bool IsConfirmation { get; private set; } = true;

    /// <summary>Label of the closing button: "Cancel" while deciding, "Close" while reading.</summary>
    public string DismissText => IsConfirmation ? Loc.Tr("PluginStore_Cancel") : Loc.Tr("PluginStore_Close");

    public CommunityToolkit.Mvvm.Input.IRelayCommand ConfirmCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    });

    public CommunityToolkit.Mvvm.Input.IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    /// <param name="confirmation">False shows the notes without offering to install.</param>
    public void Initialize(PluginStoreItem item, bool confirmation = true)
    {
        // Reading the notes of something already installed still has a release to read: fall
        // back to the newest one the catalog knows.
        _candidate = item.Available ?? item.Newest;
        IsConfirmation = confirmation;
        bool isUpdate = item.Installed is not null;

        Headline = isUpdate
            ? Loc.Tr("PluginStore_UpdateHeadline", item.Entry.DisplayName, item.InstalledVersion, _candidate.Version)
            : Loc.Tr("PluginStore_InstallHeadline", item.Entry.DisplayName, _candidate.Version);
        Subtitle = _candidate.Tag;
        Notes = Loc.Tr("PluginStore_LoadingReleaseNotes");
        ConfirmText = isUpdate ? Loc.Tr("PluginStore_Update") : Loc.Tr("PluginStore_Install");
    }

    /// <summary>
    /// Fills in the release notes once they arrive. Install stays possible throughout: a lookup that fails
    /// only replaces the notes with a pointer to the release page.
    /// </summary>
    public async Task LoadNotesAsync(CancellationToken cancellationToken = default)
    {
        if (_candidate is null)
        {
            return;
        }

        string notes = await store.GetReleaseNotesAsync(_candidate, cancellationToken);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
            return;
        }

        string url = _candidate.Release.ReleaseNotesUrl;
        Notes = string.IsNullOrWhiteSpace(url)
            ? Loc.Tr("Update_NoReleaseNotes")
            : Loc.Tr("PluginStore_ReleaseNotesUnavailable", url);
    }
}
