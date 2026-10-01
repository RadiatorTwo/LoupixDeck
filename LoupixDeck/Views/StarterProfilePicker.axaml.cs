using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class StarterProfilePicker : Window
{
    public StarterProfilePicker()
    {
        InitializeComponent();

        // Closing via the window chrome counts as "nothing created".
        Closing += (_, _) =>
        {
            if (DataContext is StarterProfilePickerViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire the VM's
    // close request to the window.
    public StarterProfilePicker(StarterProfilePickerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}