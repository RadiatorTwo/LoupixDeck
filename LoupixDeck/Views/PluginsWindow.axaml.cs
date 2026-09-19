using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.Plugins;

namespace LoupixDeck.Views;

/// <summary>
/// Installed plugins and the Plugin Store, in a window of their own rather than as two pages
/// of the device Settings window.
/// </summary>
public partial class PluginsWindow : Window
{
    private bool _closeConfirmed;

    public PluginsWindow() : this(null) { }

    public PluginsWindow(PluginsWindowViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        Closing += OnClosing;
        Closed += (_, _) => (DataContext as PluginsWindowViewModel)?.Installed.Cleanup();
    }

    /// <summary>
    /// Unsaved plugin settings must not disappear with the window, so closing is held back until
    /// the question is answered. Avalonia cannot await a Closing handler, so the close is
    /// cancelled, the question asked, and the window closed again once the answer is in.
    /// </summary>
    private async void OnClosing(object sender, WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && DataContext is PluginsWindowViewModel vm)
        {
            e.Cancel = true;
            if (!await vm.Installed.ConfirmCloseAsync())
                return;

            _closeConfirmed = true;
            Close();
            return;
        }

        if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
        {
            dlg.DialogResult.TrySetResult(new DialogResult(false));
        }
    }
}
