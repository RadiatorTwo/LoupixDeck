using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class RotaryButtonSettings : Window
{
    private TextBox _lastFocusedTextBox;

    public RotaryButtonSettings()
    {
        InitializeComponent();

        this.Closing += (s, e) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RotaryButtonSettingsViewModel vm) return;

        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            if (e.ClickCount != 2) return;

            vm.InsertCommand(menuEntry, GetActiveSlot(vm));
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

    public void TextBoxGotFocus(object sender, FocusChangedEventArgs e)
    {
        _lastFocusedTextBox = sender as TextBox;

        if (DataContext is RotaryButtonSettingsViewModel vm)
        {
            if (_lastFocusedTextBox == TextBoxRotaryLeft)
                vm.SelectedCommandSlot = RotaryButtonSettingsViewModel.SelectedCommand.RotaryLeft;
            else if (_lastFocusedTextBox == TextBoxRotaryRight)
                vm.SelectedCommandSlot = RotaryButtonSettingsViewModel.SelectedCommand.RotaryRight;
            else if (_lastFocusedTextBox == TextBoxButtonPress)
                vm.SelectedCommandSlot = RotaryButtonSettingsViewModel.SelectedCommand.ButtonPress;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RotaryButtonSettingsViewModel vm) return;
        if (sender is not Button { Tag: string tag }) return;

        if (Enum.TryParse<RotaryButtonSettingsViewModel.SelectedCommand>(tag, out var slot))
        {
            vm.ClearSlot(slot);
        }
    }

    private RotaryButtonSettingsViewModel.SelectedCommand GetActiveSlot(RotaryButtonSettingsViewModel vm)
    {
        if (_lastFocusedTextBox == TextBoxRotaryLeft)
            return RotaryButtonSettingsViewModel.SelectedCommand.RotaryLeft;
        if (_lastFocusedTextBox == TextBoxRotaryRight)
            return RotaryButtonSettingsViewModel.SelectedCommand.RotaryRight;
        if (_lastFocusedTextBox == TextBoxButtonPress)
            return RotaryButtonSettingsViewModel.SelectedCommand.ButtonPress;

        return vm.SelectedCommandSlot;
    }
}
