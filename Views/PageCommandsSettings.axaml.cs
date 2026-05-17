using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class PageCommandsSettings : Window
{
    public PageCommandsSettings()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is PageCommandsSettingsViewModel vm)
                vm.CloseRequested += Close;
        };

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    // Track which command TextBox the user clicked into last; double-clicking a
    // tree entry appends the formatted command to that box.
    private void OnCommandBoxFocused(object sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not WrapSlot slot) return;
        if (DataContext is not PageCommandsSettingsViewModel vm) return;
        var isPost = string.Equals(tb.Tag as string, "post", StringComparison.OrdinalIgnoreCase);
        vm.SetActiveTarget(slot, isPost);
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            if (e.ClickCount == 2)
            {
                ((PageCommandsSettingsViewModel)DataContext)?.InsertCommand(menuEntry);
            }
        }
        else
        {
            var source = e.Source as Control;
            var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();
            if (treeViewItem == null || !e.GetCurrentPoint(treeViewItem).Properties.IsLeftButtonPressed) return;
            var menuEntryP = (MenuEntry)treeViewItem.DataContext;
            if (menuEntryP == null) { e.Handled = true; return; }
            if (menuEntryP.Command == null || !string.IsNullOrWhiteSpace(menuEntryP.Command)) return;
            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
            e.Handled = true;
        }
    }
}
