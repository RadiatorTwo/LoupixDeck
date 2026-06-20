using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels.Base;

public interface IDialogViewModel
{
    TaskCompletionSource<DialogResult> DialogResult { get; }
}

public class ViewModelBase : ObservableObject
{
}
