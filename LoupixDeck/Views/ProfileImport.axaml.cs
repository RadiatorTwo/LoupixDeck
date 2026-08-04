using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class ProfileImport : Window
{
    public ProfileImport()
    {
        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is not ProfileImportViewModel vm)
                return;

            // Closing via the window chrome counts as "not imported" — and must still throw the
            // staged package away, or the temp folder leaks.
            if (!vm.DialogResult.Task.IsCompleted)
            {
                vm.DiscardAnalysis();
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire the VM's
    // close request to the window.
    public ProfileImport(ProfileImportViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
