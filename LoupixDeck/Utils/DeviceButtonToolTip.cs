using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// Tooltip on the touch tiles, LED buttons and dials of a device layout, naming the assigned
/// commands (issues #306, #685). The text is resolved just before the tooltip opens, so command
/// edits and plugin reloads are always current without a binding per button. Each hooked button
/// gets a placeholder <c>ToolTip.Tip</c>, because Avalonia only raises <c>ToolTipOpening</c> while
/// a tip is set.
/// </summary>
public static class DeviceButtonToolTip
{
    private const string Placeholder = " ";

    /// <summary>Hooks every touch tile (<c>Tag="Touch"</c>), LED button (<c>Tag="Led"</c>) and dial
    /// (<c>Dial</c> / <c>Knob</c> class) of <paramref name="layout"/>. A button that declares its own
    /// tooltip, such as the CT's centre wheel, keeps it.</summary>
    public static void Attach(Control layout)
    {
        foreach (Button button in layout.GetLogicalDescendants().OfType<Button>())
        {
            bool isDeviceButton = Equals(button.Tag, "Touch") || Equals(button.Tag, "Led") ||
                                  button.Classes.Contains("Dial") || button.Classes.Contains("Knob");
            if (!isDeviceButton || ToolTip.GetTip(button) is not (null or Placeholder))
                continue;

            ToolTip.SetTip(button, Placeholder);
            ToolTip.AddToolTipOpeningHandler(button, OnToolTipOpening);
        }
    }

    private static void OnToolTipOpening(object sender, CancelRoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not MainWindowViewModel viewModel)
            return;

        string text = button.CommandParameter switch
        {
            TouchButton { IsFolderNavTile: true } => null,
            RotaryButton dial => viewModel.DescribeRotary(dial),
            LoupedeckButton other => viewModel.DescribeCommand(other.Command),
            _ => null
        };

        if (text == null)
        {
            e.Cancel = true;
            return;
        }

        ToolTip.SetTip(button, text);
    }
}
