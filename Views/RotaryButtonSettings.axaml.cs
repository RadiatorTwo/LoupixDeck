using Avalonia.Controls;
using Avalonia.Input;
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

    public RotaryButtonSettings(IObsController obs,
        ElgatoDevices elgatoDevices,
        ISysCommandService sysCommandService,
        ICommandBuilder commandBuilder)
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
        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            if (e.ClickCount != 2) return;

            if (_lastFocusedTextBox == null)
            {
                ((RotaryButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry,
                    RotaryButtonSettingsViewModel.SelectedCommand.RotaryLeft);
            }
            else
            {
                if (_lastFocusedTextBox == TextBoxRotaryLeft)
                {
                    ((RotaryButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry,
                        RotaryButtonSettingsViewModel.SelectedCommand.RotaryLeft);
                }
                else if (_lastFocusedTextBox == TextBoxRotaryRight)
                {
                    ((RotaryButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry,
                        RotaryButtonSettingsViewModel.SelectedCommand.RotaryRight);
                }
                else if (_lastFocusedTextBox == TextBoxButtonPress)
                {
                    ((RotaryButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry,
                        RotaryButtonSettingsViewModel.SelectedCommand.ButtonPress);
                }
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

    public void TextBoxGotFocus(object sender, GotFocusEventArgs e)
    {
        _lastFocusedTextBox = sender as TextBox;
    }
}