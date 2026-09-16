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
    public PluginsWindow() : this(null) { }

    public PluginsWindow(PluginsWindowViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
            {
                dlg.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }
}
