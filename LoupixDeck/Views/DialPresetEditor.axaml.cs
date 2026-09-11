using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

/// <summary>Names a dial preset. Purely markup; everything happens in the view model.</summary>
public partial class DialPresetEditor : Window
{
    public DialPresetEditor()
    {
        InitializeComponent();

        // Closing via the window chrome (X) without a button counts as "not saved".
        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire the view
    // model's close request to the window, which is what actually ends the dialog.
    public DialPresetEditor(DialPresetEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
    }
}
