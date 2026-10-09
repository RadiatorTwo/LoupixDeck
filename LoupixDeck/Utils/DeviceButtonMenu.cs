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

        menu.Items.Add(MakeItem(Loc.Tr("ButtonMenu_Copy"), vm.CopySelectedCommand, vm.CanCopySelected()));
        menu.Items.Add(MakeItem(Loc.Tr("ButtonMenu_Cut"), vm.CutSelectedCommand, vm.CanClearSelected()));
        menu.Items.Add(MakeItem(Loc.Tr("ButtonMenu_Paste"), vm.PasteSelectedCommand, vm.CanPasteSelected()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(Loc.Tr("ButtonMenu_Clear"), vm.ClearSelectedCommand, vm.CanClearSelected()));

        menu.ShowAt(button, showAtPointer: true);
        e.Handled = true;
    }

    /// <summary>
    /// The "Layout template" submenu: the four templates the button editor offers for the clicked key,
    /// then the same four for every key of the page. It stays open on an empty key, because the page
    /// entries do not depend on the key that was clicked.
    /// </summary>
    private static MenuItem BuildTemplateItem(MainWindowViewModel vm)
    {
        MenuItem item = new() { Header = Loc.Tr("TouchButton_LayoutTemplate") };
        AddTemplateItems(item, vm.ApplyTemplateToSelectedCommand, vm.CanApplyTemplateToSelected());

        item.Items.Add(new Separator());

        MenuItem page = new()
        {
            Header = Loc.Tr("TouchButton_TemplateWholePage"),
            IsEnabled = vm.CanApplyTemplateToPage()
        };
        AddTemplateItems(page, vm.ApplyTemplateToPageCommand, enabled: true);
        item.Items.Add(page);

        return item;
    }

    private static void AddTemplateItems(MenuItem parent, ICommand command, bool enabled)
    {
        parent.Items.Add(MakeTemplateItem("TouchButton_Template_IconTextBottom", ButtonTemplate.IconCaptionBottom, command, enabled));
        parent.Items.Add(MakeTemplateItem("TouchButton_Template_IconTextTop", ButtonTemplate.IconCaptionTop, command, enabled));
        parent.Items.Add(MakeTemplateItem("TouchButton_Template_IconOnly", ButtonTemplate.IconOnly, command, enabled));
        parent.Items.Add(MakeTemplateItem("TouchButton_Template_TextOnly", ButtonTemplate.TextOnly, command, enabled));
    }

    private static MenuItem MakeTemplateItem(string headerKey, ButtonTemplate template, ICommand command,
        bool enabled)
        => new() { Header = Loc.Tr(headerKey), Command = command, CommandParameter = template, IsEnabled = enabled };

    private static MenuItem MakeItem(string header, ICommand command, bool enabled)
        => new() { Header = header, Command = command, IsEnabled = enabled };
}
