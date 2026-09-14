using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class PluginReleaseNotesDialog : Window
{
    public PluginReleaseNotesDialog()
    {
        InitializeComponent();
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire
    // the VM's close request to the window.
    public PluginReleaseNotesDialog(PluginReleaseNotesViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
