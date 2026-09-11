using Avalonia.Controls;

namespace LoupixDeck.Views;

/// <summary>
/// The main window's apps and actions side panel. Purely markup: the rows are picked up by the
/// window-level drag machine, so there is no pointer handling to do here.
/// </summary>
public partial class ActionPanelView : UserControl
{
    public ActionPanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The embedded command picker, so the hosting window can wire its drag and activation events
    /// to the deck. It lives inside this control's own name scope, which the window cannot reach.
    /// </summary>
    public CommandPickerView CommandPicker => this.FindControl<CommandPickerView>("CommandPickerControl");
}
