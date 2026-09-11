using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.ActionPanel;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.Views;

/// <summary>
/// The apps and actions panel, as its own borderless window flanged to the left of the main
/// window and moving with it.
/// </summary>
/// <remarks>
/// It is a separate window so that opening the panel never changes the main window's size or
/// position. Every in-window variant had to widen the main window, and Avalonia applies a window's
/// size and its content in separate passes, so the window rendered resized before it rendered its
/// new content — which is what read as flickering.
///
/// The cost is that a drag from a row to a key crosses a window boundary. The pointer is captured
/// here for the whole gesture, so the events keep arriving; they are translated into the main
/// window's coordinates and handed to its drag machine, which then behaves exactly as it does for
/// a drag that started inside it.
/// </remarks>
public partial class ActionPanelWindow : Window
{
    /// <summary>Gap between the panel and the main window, so the two read as one surface.</summary>
    private const int Gap = 0;

    private Window _owner;
    private DeviceDragDrop _dragDrop;
    private bool _dragArmed;
    private bool _commandDragging;

    private Canvas _overlay;
    private Border _ghost;
    private Image _ghostImage;

    public ActionPanelWindow()
    {
        InitializeComponent();

        _overlay = this.FindControl<Canvas>("DragOverlay");
        _ghost = this.FindControl<Border>("DragGhost");
        _ghostImage = this.FindControl<Image>("DragGhostImage");

        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPreviewPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Binds this panel to the main window: it takes the main window's height, sits against its
    /// left edge, and follows it as it is moved or resized.
    /// </summary>
    public void Attach(Window owner, DeviceDragDrop dragDrop)
    {
        _owner = owner;
        _dragDrop = dragDrop;

        owner.PositionChanged += (_, _) => FollowOwner();
        owner.Resized += (_, _) => FollowOwner();

        WireCommandPicker();
        FollowOwner();
    }

    // ── The embedded command picker ────────────────────────────────────────
    //
    // It runs its own press/threshold/capture bookkeeping and reports a drag as three events, so
    // the panel only has to translate the positions and hand the command on.

    private void WireCommandPicker()
    {
        CommandPickerView picker = this.FindControl<ActionPanelView>("PanelView")?.CommandPicker;
        if (picker == null) return;

        picker.CommandActivated += OnCommandActivated;
        picker.CommandDragStarted += OnCommandDragStarted;
        picker.CommandDragMoved += OnCommandDragMoved;
        picker.CommandDragReleased += OnCommandDragReleased;
    }

    private void OnCommandActivated(object sender, MenuEntry entry)
        => _dragDrop?.PanelAssignToSelection(new ActionPanelItemViewModel(entry));

    private void OnCommandDragStarted(object sender, CommandDragEventArgs e)
    {
        if (_owner == null || _dragDrop == null) return;

        _commandDragging = _dragDrop.PanelDragBegin(
            new ActionPanelItemViewModel(e.Entry, RowGlyph(e.Row)), e.Row, ToOwner(e.Pointer));
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
        if (!_commandDragging) return;

        _dragDrop.PanelPointerMoved(ToOwner(e.Pointer));
        UpdateGhost(e.Pointer);
    }

    private void OnCommandDragReleased(object sender, CommandDragEventArgs e)
    {
        if (!_commandDragging) return;

        Point position = ToOwner(e.Pointer);
        _commandDragging = false;
        HideGhost();
        _dragDrop.PanelPointerReleased(position);
    }

    /// <summary>Matches the owner's height and parks against its left edge.</summary>
    public void FollowOwner()
    {
        if (_owner == null) return;

        Size frame = _owner.FrameSize ?? _owner.Bounds.Size;
        if (frame.Height > 0)
            Height = frame.Height;

        int width = (int)Math.Round((FrameSize?.Width ?? Width) * _owner.RenderScaling);
        Position = new PixelPoint(_owner.Position.X - width - Gap, _owner.Position.Y);
    }

    // ── Drags out of the panel ─────────────────────────────────────────────
    //
    // The gesture is owned by this window (it captures the pointer), but the target is in the main
    // window, so every position is translated into that window's coordinates before it is handed on.

    private Point ToOwner(PointerEventArgs e)
        => _owner.PointToClient(this.PointToScreen(e.GetPosition(this)));

    private void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (_owner == null || _dragDrop == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragArmed = _dragDrop.PanelPointerPressed(e.Source as Visual, ToOwner(e));

        // Capture here for the whole gesture: without it the events stop the moment the pointer
        // leaves this window, which is exactly where the drag is going.
        if (_dragArmed)
            e.Pointer.Capture(this);
    }

    private void OnPreviewPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_dragArmed) return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag(e.Pointer);
            return;
        }

        _dragDrop.PanelPointerMoved(ToOwner(e));
        UpdateGhost(e);
    }

    /// <summary>
    /// Draws the drag ghost while the pointer is still over the panel. The main window takes over
    /// the moment the pointer crosses into it: a visual lives in one window, so the two overlays
    /// hand the ghost between them rather than sharing it.
    /// </summary>
    private void UpdateGhost(PointerEventArgs e)
    {
        IImage image = _dragDrop.PanelDragGhost;
        Point position = e.GetPosition(this);
        bool over = image != null && new Rect(Bounds.Size).Contains(position);

        if (!over)
        {
            HideGhost();
            return;
        }

        Size size = _dragDrop.PanelDragGhostSize;
        _ghostImage.Source = image;
        _ghost.Width = size.Width;
        _ghost.Height = size.Height;
        _ghost.Padding = _dragDrop.PanelDragGhostPadding;

        // Centred on the cursor, the way the main window hangs a panel ghost.
        Canvas.SetLeft(_ghost, position.X - (size.Width / 2));
        Canvas.SetTop(_ghost, position.Y - (size.Height / 2));

        _overlay.IsVisible = true;
        _ghost.IsVisible = true;
    }

    private void HideGhost()
    {
        if (_ghost == null) return;

        _ghostImage.Source = null;
        _ghost.IsVisible = false;
        _overlay.IsVisible = false;
    }

    private void OnPreviewPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragArmed) return;

        Point position = ToOwner(e);
        _dragArmed = false;
        HideGhost();
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
        HideGhost();
        pointer?.Capture(null);
        _dragDrop.PanelDragCancelled();
    }
}
