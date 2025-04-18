using LoupixDeck.Models;

namespace LoupixDeck.ViewModels.Base;

public abstract class DialogViewModelBase<TResult> : ViewModelBase, IDialogViewModel
{
    public TaskCompletionSource<DialogResult> DialogResult { get; } = new();

    protected void Confirm(TResult result)
    {
        DialogResult.TrySetResult(new DialogResult(true));
    }

    protected void Cancel()
    {
        DialogResult.TrySetResult(new DialogResult(false));
    }
}

public abstract class DialogViewModelBase<TParam, TResult> : ViewModelBase, IDialogViewModel
{
    public TaskCompletionSource<DialogResult> DialogResult { get; } = new();

    public abstract void Initialize(TParam parameter);

    protected void Confirm(TResult result)
    {
        DialogResult.TrySetResult(new DialogResult(true));
    }

    protected void Cancel()
    {
        DialogResult.TrySetResult(new DialogResult(false));
    }
}