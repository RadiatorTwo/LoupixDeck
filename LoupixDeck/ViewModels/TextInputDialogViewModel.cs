using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Single-line text prompt, e.g. for naming a profile. Configure it before showing; confirm stays
/// disabled while the text is blank. Read <see cref="Result"/> after a confirmed
/// <see cref="DialogViewModelBase{TResult}.DialogResult"/>.
/// </summary>
public sealed partial class TextInputDialogViewModel : DialogViewModelBase<DialogResult>
{
    public string DialogTitle { get; private set; } = string.Empty;
    public string Placeholder { get; private set; } = string.Empty;
    public string ConfirmText { get; private set; } = string.Empty;
    public string CancelText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>The entered text without surrounding whitespace.</summary>
    public string Result => Text?.Trim() ?? string.Empty;

    public IRelayCommand ConfirmCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    }, () => !string.IsNullOrWhiteSpace(Text));

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    public void Configure(string title, string placeholder, string confirmText, string initialText = null)
    {
        DialogTitle = title ?? string.Empty;
        Placeholder = placeholder ?? string.Empty;
        ConfirmText = confirmText ?? Loc.Tr("Confirm_Yes");
        CancelText = Loc.Tr("Confirm_Cancel");
        Text = initialText ?? string.Empty;
    }
}
