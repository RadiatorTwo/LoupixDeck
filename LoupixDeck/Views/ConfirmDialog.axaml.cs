using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();

        // Closing via the window chrome (X) without a button counts as "not confirmed".
        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire
    // the VM's close request to the window.
    public ConfirmDialog(ConfirmDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
