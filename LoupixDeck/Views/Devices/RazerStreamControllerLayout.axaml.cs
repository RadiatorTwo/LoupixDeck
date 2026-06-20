using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Views.Devices;

public partial class RazerStreamControllerLayout : UserControl
{
    public RazerStreamControllerLayout()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // The page-name text boxes bind their Name two-way (updated as you type), so a
    // commit only needs to persist the config. Enter commits and drops focus.
    private void OnPageNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PageNameEditing.Save(sender);
        e.Handled = true;
    }

    private void OnPageNameCommit(object sender, RoutedEventArgs e) => PageNameEditing.Save(sender);
}
