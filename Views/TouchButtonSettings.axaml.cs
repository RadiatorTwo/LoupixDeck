using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class TouchButtonSettings : Window
{
    public TouchButtonSettings()
    {
        DataContext = new TouchButtonSettingsViewModel(new TouchButton(-1), null, null);
        InitializeComponent();
    }

    public TouchButtonSettings(TouchButton buttonData, ObsController obs, ElgatoDevices elgatoDevices)
    {
        DataContext = new TouchButtonSettingsViewModel(buttonData, obs, elgatoDevices);
        InitializeComponent();
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBlock textBlock && textBlock.DataContext is SystemCommand command && command.IsCommand)
        {
            if (e.ClickCount == 2)
            {
                ((TouchButtonSettingsViewModel)DataContext)?.InsertCommand(command);
            }
        }
        else
        {
            var source = e.Source as Control;
            var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();

            if (treeViewItem == null || !e.GetCurrentPoint(treeViewItem).Properties.IsLeftButtonPressed) return;
            var sysCommand = (SystemCommand)treeViewItem.DataContext;

            if (sysCommand == null || !sysCommand.IsCommand) return;

            // Toggle Auf-/Zuklappen
            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;

            // Optional: Verhindert doppelte Auswahländerung
            e.Handled = true;
        }
    }
}