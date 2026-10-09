using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.Actions;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// Shared right-click context menu for the device buttons (issue #166). Attached via a single
/// <c>ContextRequested</c> handler on each layout root; the event bubbles up from the button, so
/// no per-button wiring is needed. Right-clicking a button selects it first, then opens a
/// Copy / Cut / Paste / Clear menu that acts on the current selection.
///
/// A dial gets the quick menu above those entries: its three gestures, the presets, and a way into
/// the full editor. Those items are built by <see cref="DialQuickMenu"/> — this stays the one place
/// a device button's menu is composed, and each kind of button contributes its own section.
/// A grid touch button gets the layout templates (issue #370) the same way.
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

        // The automatic Back tile of a custom folder has nothing to copy, paste or clear.
        if (target is TouchButton { IsFolderBackSlot: true })
        {
            vm.SelectButton(null);
            e.Handled = true;
            return;
        }

        vm.SelectButton(target);

        MenuFlyout menu = new();

        if (target is RotaryButton dial)
        {
            foreach (Control item in DialQuickMenu.BuildItems(dial, vm))
                menu.Items.Add(item);

            menu.Items.Add(new Separator());
        }

        if (vm.IsTemplateTarget(target))
        {
            menu.Items.Add(BuildTemplateItem(vm));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MakeItem("Copy", vm.CopySelectedCommand, vm.CanCopySelected()));
        menu.Items.Add(MakeItem("Cut", vm.CutSelectedCommand, vm.CanClearSelected()));
        menu.Items.Add(MakeItem("Paste", vm.PasteSelectedCommand, vm.CanPasteSelected()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Clear", vm.ClearSelectedCommand, vm.CanClearSelected()));

        menu.ShowAt(button, showAtPointer: true);
        e.Handled = true;
    }

    /// <summary>The "Layout template" submenu with the four templates the button editor offers.</summary>
    private static MenuItem BuildTemplateItem(MainWindowViewModel vm)
    {
        MenuItem item = new()
        {
            Header = Loc.Tr("TouchButton_LayoutTemplate"),
            IsEnabled = vm.CanApplyTemplateToSelected()
        };

        item.Items.Add(MakeTemplateItem("TouchButton_Template_IconTextBottom", ButtonTemplate.IconCaptionBottom, vm));
        item.Items.Add(MakeTemplateItem("TouchButton_Template_IconTextTop", ButtonTemplate.IconCaptionTop, vm));
        item.Items.Add(MakeTemplateItem("TouchButton_Template_IconOnly", ButtonTemplate.IconOnly, vm));
        item.Items.Add(MakeTemplateItem("TouchButton_Template_TextOnly", ButtonTemplate.TextOnly, vm));
        return item;
    }

    private static MenuItem MakeTemplateItem(string headerKey, ButtonTemplate template, MainWindowViewModel vm)
        => new() { Header = Loc.Tr(headerKey), Command = vm.ApplyTemplateToSelectedCommand, CommandParameter = template };

    private static MenuItem MakeItem(string header, ICommand command, bool enabled)
        => new() { Header = header, Command = command, IsEnabled = enabled };
}
