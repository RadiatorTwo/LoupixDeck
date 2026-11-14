using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>One target the converted item can be placed in.</summary>
/// <param name="Name">Name shown in the list.</param>
/// <param name="Value">The caller's object behind the entry.</param>
/// <param name="IsSource">True when this target is the item being converted, which then has to be kept.</param>
public sealed record ConvertTarget(string Name, object Value, bool IsSource);

/// <summary>
/// The dialog of a workspace-to-folder or folder-to-workspace conversion (issue #606): explains
/// what the conversion changes, offers a target when there is a choice, and asks whether the
/// source is kept or discarded afterwards. Read <see cref="SelectedTarget"/> and
/// <see cref="KeepSource"/> after a confirmed <see cref="DialogViewModelBase{TResult}.DialogResult"/>.
/// </summary>
public sealed partial class ConvertDialogViewModel : DialogViewModelBase<DialogResult>
{
    public string DialogTitle { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string TargetLabel { get; private set; } = string.Empty;
    public string KeepText { get; private set; } = string.Empty;
    public string DiscardText { get; private set; } = string.Empty;
    public string ConfirmText { get; private set; } = string.Empty;
    public string CancelText { get; private set; } = string.Empty;

    public IReadOnlyList<ConvertTarget> Targets { get; private set; } = [];

    /// <summary>True when the dialog shows the target list.</summary>
    public bool HasTargets => Targets.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDiscard))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial ConvertTarget SelectedTarget { get; set; }

    [ObservableProperty]
    public partial bool KeepSource { get; set; } = true;

    /// <summary>False when the source is the chosen target, since it then has to stay.</summary>
    public bool CanDiscard => SelectedTarget is not { IsSource: true };

    partial void OnSelectedTargetChanged(ConvertTarget value)
    {
        if (value is { IsSource: true })
            KeepSource = true;
    }

    public IRelayCommand ConfirmCommand => field ??= Relay.Create(() =>
    {
        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    }, () => Targets.Count == 0 || SelectedTarget != null);

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    /// <param name="targets">Where the result can go; empty when there is no choice to make.</param>
    public void Configure(string title, string message, string confirmText, string keepText, string discardText,
        IReadOnlyList<ConvertTarget> targets = null, string targetLabel = null)
    {
        DialogTitle = title ?? string.Empty;
        Message = message ?? string.Empty;
        ConfirmText = confirmText ?? Loc.Tr("Confirm_Yes");
        CancelText = Loc.Tr("Confirm_Cancel");
        KeepText = keepText ?? string.Empty;
        DiscardText = discardText ?? string.Empty;
        TargetLabel = targetLabel ?? string.Empty;
        Targets = targets ?? [];
        SelectedTarget = Targets.FirstOrDefault(static t => !t.IsSource) ?? Targets.FirstOrDefault();
    }
}
