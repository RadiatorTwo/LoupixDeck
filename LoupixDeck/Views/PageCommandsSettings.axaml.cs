using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class PageCommandsSettings : Window
{
    public PageCommandsSettings()
    {
        InitializeComponent();

        // Card-based command picker → focused command box (issue #171).
        PickerControl.CommandActivated += Picker_CommandActivated;

        Opened += (_, _) =>
        {
            if (DataContext is PageCommandsSettingsViewModel vm)
                vm.CloseRequested += Close;
        };

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));

            if (DataContext is PageCommandsSettingsViewModel pvm)
                pvm.Cleanup();
        };
    }

    // Track which command TextBox the user clicked into last; double-clicking a
    // command appends the formatted command to that box.
    private void OnCommandBoxFocused(object sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not WrapSlot slot) return;
        if (DataContext is not PageCommandsSettingsViewModel vm) return;
        var isPost = string.Equals(tb.Tag as string, "post", StringComparison.OrdinalIgnoreCase);
        vm.SetActiveTarget(slot, isPost);
    }

    private void Picker_CommandActivated(object sender, MenuEntry entry)
    {
        if (entry != null && DataContext is PageCommandsSettingsViewModel vm)
            vm.InsertCommand(entry);
    }
}