using Avalonia.Controls;
using Avalonia.Input;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class SymbolPicker : Window
{
    public SymbolPicker()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is SymbolPickerViewModel vm)
                vm.CloseRequested += Close;
        };

        Closing += (_, _) =>
        {
            // Closing via the window chrome (X) counts as a cancel.
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    private void SymbolList_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is SymbolPickerViewModel vm)
            vm.ConfirmSelection();
    }
}
