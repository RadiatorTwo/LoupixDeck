using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Release notes of the plugin version the store is about to install or update to (issue #234), with
/// Install / Cancel. Nothing is downloaded before the user confirms.
/// </summary>
public sealed class PluginReleaseNotesViewModel : DialogViewModelBase<DialogResult>
{
    public string Headline { get; private set; } = string.Empty;

    public string Subtitle { get; private set; } = string.Empty;

    public string Notes { get; private set; } = string.Empty;

    public string ConfirmText { get; private set; } = string.Empty;

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

    public void Initialize(PluginStoreItem item)
    {
        PluginReleaseCandidate candidate = item.Available;
        bool isUpdate = item.Installed is not null;

        Headline = isUpdate
            ? Loc.Tr("PluginStore_UpdateHeadline", item.Entry.DisplayName, item.InstalledVersion, candidate.Version)
            : Loc.Tr("PluginStore_InstallHeadline", item.Entry.DisplayName, candidate.Version);
        Subtitle = string.IsNullOrWhiteSpace(candidate.Release.Name) || candidate.Release.Name == candidate.Release.Tag
            ? candidate.Release.Tag
            : $"{candidate.Release.Tag} - {candidate.Release.Name}";
        Notes = string.IsNullOrWhiteSpace(candidate.Release.Notes)
            ? Loc.Tr("Update_NoReleaseNotes")
            : candidate.Release.Notes.Trim();
        ConfirmText = isUpdate ? Loc.Tr("PluginStore_Update") : Loc.Tr("PluginStore_Install");
    }
}
