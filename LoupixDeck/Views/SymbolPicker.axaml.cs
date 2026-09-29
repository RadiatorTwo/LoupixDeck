using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
            if (DataContext is not SymbolPickerViewModel vm)
                return;

            vm.CloseRequested += Close;
            vm.ScrollToRowRequested += ScrollToRow;
            if (vm.InitialRowIndex >= 0)
                ScrollToRow(vm.InitialRowIndex);
        };

        Closed += (_, _) =>
        {
            if (DataContext is not SymbolPickerViewModel vm)
                return;

            vm.CloseRequested -= Close;
            vm.ScrollToRowRequested -= ScrollToRow;
            vm.ReleaseResources();
        };

        Closing += (_, _) =>
        {
            // Closing via the window chrome (X) counts as a cancel.
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    private void ScrollToRow(int row)
    {
        Dispatcher.UIThread.Post(() => SymbolRows.ScrollIntoView(row), DispatcherPriority.Loaded);
    }

    private void SymbolCell_Tapped(object sender, TappedEventArgs e)
    {
        if (DataContext is SymbolPickerViewModel vm && sender is Control { DataContext: SymbolCell cell })
            vm.SelectCell(cell);
    }

    private void SymbolCell_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is SymbolPickerViewModel vm && sender is Control { DataContext: SymbolCell cell })
        {
            vm.SelectCell(cell);
            vm.ConfirmSelection();
        }
    }
}
