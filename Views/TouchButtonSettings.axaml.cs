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
        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry && menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            if (e.ClickCount == 2)
            {
                ((TouchButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry);
            }
        }
        else
        {
            var source = e.Source as Control;
            var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();

            if (treeViewItem == null || !e.GetCurrentPoint(treeViewItem).Properties.IsLeftButtonPressed) return;
            var menuEntryP = (MenuEntry)treeViewItem.DataContext;

            if (menuEntryP == null)
            {
                e.Handled = true;
                return;
            }

            if (menuEntryP.Command == null || !string.IsNullOrWhiteSpace(menuEntryP.Command)) return;

            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;

            e.Handled = true;
        }
    }
}