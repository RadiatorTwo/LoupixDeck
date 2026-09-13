using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    // Preferred ctor (see DialogService): set DataContext before the XAML pass and wire
    // the VM's close request to the window.
    public UpdateDialog(UpdateDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindow += Close;
        // Closing by the title bar X must not let a running download go on to start the installer.
        Closed += (_, _) => viewModel.CancelDownload();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
