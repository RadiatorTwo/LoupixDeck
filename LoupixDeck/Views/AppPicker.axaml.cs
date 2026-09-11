using Avalonia.Controls;
using Avalonia.Input;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class AppPicker : Window
{
    public AppPicker()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is AppPickerViewModel vm)
                vm.CloseRequested += Close;
        };

        Closing += (_, _) =>
        {
            // Closing via the window chrome (X) counts as a cancel.
            if (DataContext is IDialogViewModel dialog && !dialog.DialogResult.Task.IsCompleted)
                dialog.DialogResult.TrySetResult(new DialogResult(false));

            // Stop the icon stream however the window was closed — the X never reaches
            // Confirm or Cancel, and a few hundred extractions would keep running.
            if (DataContext is AppPickerViewModel vm)
                vm.StopLoading();
        };
    }

    private void AppList_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is AppPickerViewModel vm)
            vm.ConfirmSelection();
    }
}