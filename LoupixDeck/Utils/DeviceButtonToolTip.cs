using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// Tooltip on the touch tiles of a device layout, naming the assigned command (issue #306). The
/// text is resolved just before the tooltip opens, so command edits and plugin reloads are always
/// current without a binding per tile. The layout's touch style sets a placeholder
/// <c>ToolTip.Tip</c>, because Avalonia only raises <c>ToolTipOpening</c> while a tip is set.
/// </summary>
public static class DeviceButtonToolTip
{
    /// <summary>Hooks every touch tile (<c>Tag="Touch"</c>) of <paramref name="layout"/>.</summary>
    public static void Attach(Control layout)
    {
        foreach (Button button in layout.GetLogicalDescendants().OfType<Button>())
        {
            if (Equals(button.Tag, "Touch"))
                ToolTip.AddToolTipOpeningHandler(button, OnToolTipOpening);
        }
    }

    private static void OnToolTipOpening(object sender, CancelRoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: TouchButton touch } button)
            return;

        string text = touch.IsFolderBackSlot
            ? null
            : (button.DataContext as MainWindowViewModel)?.DescribeCommand(touch.Command);

        if (text == null)
        {
            e.Cancel = true;
            return;
        }

        ToolTip.SetTip(button, text);
    }
}
