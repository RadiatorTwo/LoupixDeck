using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// Shared right-click context menu for the device buttons (issue #166). Attached via a single
/// <c>ContextRequested</c> handler on each layout root; the event bubbles up from the button, so
/// no per-button wiring is needed. Right-clicking a button selects it first, then opens a
/// Copy / Cut / Paste / Clear menu that acts on the current selection.
/// </summary>
public static class DeviceButtonMenu
{
    public static void HandleContextRequested(ContextRequestedEventArgs e, MainWindowViewModel vm)
    {
        if (vm == null) return;

        // Walk up from the clicked element to the nearest interactive device button (one whose
        // CommandParameter is a LoupedeckButton). Anything else (chrome, pagers, empty body)
        // clears the selection and shows no menu.
        Button button = (e.Source as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<Button>()
            .FirstOrDefault(b => b.CommandParameter is LoupedeckButton);

        if (button?.CommandParameter is not LoupedeckButton target)
        {
            vm.SelectButton(null);
            return;
        }

        vm.SelectButton(target);

        MenuFlyout menu = new();
        menu.Items.Add(MakeItem("Copy", vm.CopySelectedCommand, vm.CanCopySelected()));
        menu.Items.Add(MakeItem("Cut", vm.CutSelectedCommand, vm.CanClearSelected()));
        menu.Items.Add(MakeItem("Paste", vm.PasteSelectedCommand, vm.CanPasteSelected()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Clear", vm.ClearSelectedCommand, vm.CanClearSelected()));

        menu.ShowAt(button, showAtPointer: true);
        e.Handled = true;
    }

    private static MenuItem MakeItem(string header, ICommand command, bool enabled)
        => new() { Header = header, Command = command, IsEnabled = enabled };
}
