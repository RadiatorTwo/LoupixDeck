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

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    public void Configure(string message, string title = null,
        string confirmText = null, string cancelText = null)
    {
        Message = message ?? string.Empty;
        DialogTitle = title ?? Loc.Tr("Confirm_Title");
        ConfirmText = confirmText ?? Loc.Tr("Confirm_Yes");
        CancelText = cancelText ?? Loc.Tr("Confirm_No");
    }
}
