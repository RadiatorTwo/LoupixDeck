using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.LoupedeckDevice;
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

    public TouchButtonSettings(TouchButton buttonData, ObsController obs, ElgatoController elgato)
    {
        DataContext = new TouchButtonSettingsViewModel(buttonData, obs, elgato);
        InitializeComponent();
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBlock textBlock && textBlock.DataContext is SystemCommand command &&
            command.Command != Constants.SystemCommand.NONE)
        {
            if (e.ClickCount == 2)
            {
                ((TouchButtonSettingsViewModel)DataContext)?.InsertCommand(command.Command, command.ParentName,command.Name);
            }
        }
        else
        {
            var source = e.Source as Control;
            var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();

            if (treeViewItem == null || !e.GetCurrentPoint(treeViewItem).Properties.IsLeftButtonPressed) return;
            var sysCommand = (SystemCommand)treeViewItem.DataContext;

            if (sysCommand == null || sysCommand.Command != Constants.SystemCommand.NONE) return;

            // Toggle Auf-/Zuklappen
            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;

            // Optional: Verhindert doppelte Auswahländerung
            e.Handled = true;
        }
    }
}