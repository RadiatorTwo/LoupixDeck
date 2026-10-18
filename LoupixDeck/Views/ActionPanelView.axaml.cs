using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.ActionPanel;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.Views;

/// <summary>
/// The main window's apps and actions side panel. A row dragged onto a key is assigned to it, and
/// a double-clicked row is assigned to the selected key; both through the window's drag machine.
/// </summary>
public partial class ActionPanelView : UserControl
{
    private Visual _root;
    private DeviceDragDrop _dragDrop;
    private bool _dragArmed;
    private bool _commandDragging;

    public ActionPanelView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPreviewPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// The embedded command picker. It lives inside this control's own name scope, which the
    /// window cannot reach.
    /// </summary>
    public CommandPickerView CommandPicker => this.FindControl<CommandPickerView>("CommandPickerControl");

    /// <summary>Hands the panel's drags to the window's drag machine, in <paramref name="root"/>'s coordinates.</summary>
    public void Attach(Visual root, DeviceDragDrop dragDrop)
    {
        _root = root;
        _dragDrop = dragDrop;

        if (CommandPicker is not { } picker) return;

        picker.CommandActivated += OnCommandActivated;
        picker.CommandDragStarted += OnCommandDragStarted;
        picker.CommandDragMoved += OnCommandDragMoved;
        picker.CommandDragReleased += OnCommandDragReleased;
    }

    private Point ToRoot(PointerEventArgs e) => e.GetPosition(_root);

    // ── The embedded command picker ────────────────────────────────────────
    //
    // It runs its own press/threshold/capture bookkeeping and reports a drag as three events, so
    // only the command has to be handed on.

    private void OnCommandActivated(object sender, MenuEntry entry)
        => _dragDrop?.PanelAssignToSelection(new ActionPanelItemViewModel(entry));

    private void OnCommandDragStarted(object sender, CommandDragEventArgs e)
    {
        if (_dragDrop == null) return;

        _commandDragging = _dragDrop.PanelDragBegin(
            new ActionPanelItemViewModel(e.Entry, RowGlyph(e.Row)), e.Row, ToRoot(e.Pointer));
    }

    /// <summary>
    /// The glyph the picker row is actually showing. Most commands declare none and inherit their
    /// category's, and that resolution has already happened on the row — reading the command's own
    /// icon instead would leave the majority of them without one.
    /// </summary>
    private static string RowGlyph(Control row)
        => (row?.DataContext as CommandRowViewModel)?.Icon;

    private void OnCommandDragMoved(object sender, CommandDragEventArgs e)
    {
        if (_commandDragging)
            _dragDrop.PanelPointerMoved(ToRoot(e.Pointer));
    }

    private void OnCommandDragReleased(object sender, CommandDragEventArgs e)
    {
        if (!_commandDragging) return;

        _commandDragging = false;
        _dragDrop.PanelPointerReleased(ToRoot(e.Pointer));
    }

    // ── Drags out of the app and dial preset lists ─────────────────────────

    private void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (_dragDrop == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A double-click assigns the row to the selected key; a single click only selects the row.
        if (e.ClickCount == 2)
        {
            if (PanelRowItem(e.Source as Visual) is { } item)
                _dragDrop.PanelAssignToSelection(item);
            return;
        }

        _dragArmed = _dragDrop.PanelPointerPressed(e.Source as Visual, ToRoot(e));

        // Captured for the whole gesture, so the events keep coming once the pointer leaves the
        // panel for a key. The row's list item takes the capture rather than the panel, so the
        // release still reaches it and clears its pressed state.
        if (_dragArmed)
            e.Pointer.Capture((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) ?? (IInputElement)this);
    }

    private static PanelItemViewModel PanelRowItem(Visual source)
        => source?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(static c => c.Classes.Contains("panel-row") && c.DataContext is PanelItemViewModel)
            ?.DataContext as PanelItemViewModel;

    private void OnPreviewPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_dragArmed) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer);
            return;
        }

        _dragDrop.PanelPointerMoved(ToRoot(e));
    }

    private void OnPreviewPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragArmed) return;

        Point position = ToRoot(e);
        _dragArmed = false;
        e.Pointer.Capture(null);
        _dragDrop.PanelPointerReleased(position);
    }

    private void OnPreviewPointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        if (_dragArmed)
            EndDrag(null);
    }

    private void EndDrag(IPointer pointer)
    {
        _dragArmed = false;
        pointer?.Capture(null);
        _dragDrop.PanelDragCancelled();
    }
}
