using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Small generic yes/no confirmation dialog. Configure the message (and optionally the
/// title / button captions) via <see cref="Configure"/> before showing; the result is
/// carried by the base <see cref="DialogViewModelBase{TResult}.DialogResult"/>
/// (IsConfirmed == true when the user confirms).
/// </summary>
public sealed class ConfirmDialogViewModel : DialogViewModelBase<DialogResult>
{
    public string DialogTitle { get; private set; } = "Confirm";
    public string Message { get; private set; } = string.Empty;
    public string ConfirmText { get; private set; } = "Yes";
    public string CancelText { get; private set; } = "No";

    public IRelayCommand ConfirmCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    });

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    /// <summary>
    /// The optional third choice, between confirm and cancel. It confirms the dialog and sets
    /// <see cref="AlternativeChosen"/>, so a caller that offers it reads that flag after the dialog
    /// closes; every other caller never sees it.
    /// </summary>
    public IRelayCommand AlternativeCommand => field ??= Relay.Create(() =>
    {
        AlternativeChosen = true;
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    });

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    /// <summary>False for a plain notice: only the confirm button is shown.</summary>
    public bool ShowCancel { get; private set; } = true;

    /// <summary>Caption of the third button; null hides it.</summary>
    public string AlternativeText { get; private set; }

    public bool ShowAlternative => !string.IsNullOrEmpty(AlternativeText);

    /// <summary>True when the dialog was closed through the third button.</summary>
    public bool AlternativeChosen { get; private set; }

    public void Configure(string message, string title = null,
        string confirmText = null, string cancelText = null, bool showCancel = true,
        string alternativeText = null)
    {
        AlternativeText = alternativeText;
        Message = message ?? string.Empty;
        DialogTitle = title ?? Loc.Tr("Confirm_Title");
        ConfirmText = confirmText ?? Loc.Tr("Confirm_Yes");
        CancelText = cancelText ?? Loc.Tr("Confirm_No");
        ShowCancel = showCancel;
    }
}
