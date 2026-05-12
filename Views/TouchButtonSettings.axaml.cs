using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class TouchButtonSettings : Window
{
    public TouchButtonSettings()
    {
        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TouchButtonSettingsViewModel vm) return;

        var confirmed = await ConfirmDialogHelper.AskYesNoAsync(
            this,
            "Clear Button",
            "Soll dieser Button wirklich geleert werden? Alle Einstellungen, Texte, Bilder und der Command gehen dabei verloren.");

        if (!confirmed) return;

        // Clear AFTER the window is closed — otherwise TwoWay bindings (TextBox,
        // ColorPicker) write their stale UI values back on focus loss and undo the reset.
        Closed += (_, _) => vm.ClearButton();
        Close();
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
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