using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.FolderPanel;

namespace LoupixDeck.Views;

/// <summary>
/// The custom folder panel (issue #249), as its own borderless window flanged to the right of the
/// main window and moving with it — the mirror image of <see cref="ActionPanelWindow"/>, for the
/// same reason: opening it never resizes the main window.
/// </summary>
/// <remarks>
/// A press on a folder row is resolved on release. Without movement it opens the folder. A drag
/// that ends over a key assigns the folder to it through the main window's drag machine; a drag
/// that ends over this panel moves the folder within the tree.
/// </remarks>
public partial class FolderPanelWindow : Window
{
    private const int Gap = 0;

    /// <summary>Share of a row's height at its top and bottom that means "before" / "after" instead of "into".</summary>
    private const double EdgeZone = 0.25;

    private static readonly IBrush ValidBrush = new SolidColorBrush(Color.Parse("#FF3DDB6E"));
    private static readonly IBrush InvalidBrush = new SolidColorBrush(Color.Parse("#FFE5484D"));

    private Window _owner;
    private DeviceDragDrop _dragDrop;

    private bool _dragArmed;
    private FolderNodeViewModel _pressedNode;

    private readonly Canvas _overlay;
    private readonly Border _ghost;
    private readonly Image _ghostImage;
    private readonly Border _dropIndicator;
    private readonly Border _rootRow;

    // Where a drop over the tree would put the dragged folder; null parent means the top level.
    private bool _hasTreeTarget;
    private bool _treeTargetValid;
    private FolderNodeViewModel _treeTargetParent;
    private int _treeTargetIndex;

    public FolderPanelWindow()
    {
        InitializeComponent();

        _overlay = this.FindControl<Canvas>("DragOverlay");
        _ghost = this.FindControl<Border>("DragGhost");
        _ghostImage = this.FindControl<Image>("DragGhostImage");
        _dropIndicator = this.FindControl<Border>("DropIndicator");
        _rootRow = this.FindControl<Border>("RootRow");

        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPreviewPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private FolderPanelViewModel ViewModel => DataContext as FolderPanelViewModel;

    /// <summary>Binds this panel to the main window: same height, against its right edge, following it.</summary>
    public void Attach(Window owner, DeviceDragDrop dragDrop)
    {
        _owner = owner;
        _dragDrop = dragDrop;

        owner.PositionChanged += (_, _) => FollowOwner();
        owner.Resized += (_, _) => FollowOwner();

        FollowOwner();
    }

    /// <summary>Matches the owner's height and parks against its right edge.</summary>
    public void FollowOwner()
    {
        if (_owner == null) return;

        Size frame = _owner.FrameSize ?? _owner.Bounds.Size;
        if (frame.Height > 0)
            Height = frame.Height;

        int ownerWidth = (int)Math.Round(frame.Width * _owner.RenderScaling);
        Position = new PixelPoint(_owner.Position.X + ownerWidth + Gap, _owner.Position.Y);
    }

    private void OnRootTapped(object sender, TappedEventArgs e)
        => _ = ViewModel?.CloseFoldersCommand.ExecuteAsync(null);

    // ── In-place rename ────────────────────────────────────────────────────

    private void OnRenameBoxAttached(object sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box) return;

        // The box is created per rename session, so focusing it on attach starts each edit fresh.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FolderRenameSession session } box) return;

        switch (e.Key)
        {
            case Key.Enter:
                ViewModel?.CommitRename(session, box.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel?.CancelRename(session);
                e.Handled = true;
                break;
        }
    }

    private void OnRenameBoxLostFocus(object sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: FolderRenameSession session } box)
            ViewModel?.CommitRename(session, box.Text);
    }

    // ── Press, drag, release ───────────────────────────────────────────────

    private Point ToOwner(PointerEventArgs e)
        => _owner.PointToClient(this.PointToScreen(e.GetPosition(this)));

    private static FolderNodeViewModel FolderRowNode(Visual source)
        => source?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c.Classes.Contains("folder-row") && c.DataContext is FolderNodeViewModel)
            ?.DataContext as FolderNodeViewModel;

    private void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // A press outside the text fields takes the focus away from them, which commits an open rename.
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().Any(static v => v is TextBox) != true)
            Focus();

        if (_owner == null || _dragDrop == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        FolderNodeViewModel node = FolderRowNode(e.Source as Visual);
        if (node == null) return;

        // The caret, the pencil and the rename box handle their own presses.
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().Any(static v => v is Button or TextBox) == true) return;

        if (e.ClickCount == 2)
        {
            ViewModel?.StartRename(node);
            e.Handled = true;
            return;
        }

        _dragArmed = _dragDrop.PanelPointerPressed(e.Source as Visual, ToOwner(e));
        if (!_dragArmed) return;

        _pressedNode = node;

        // Captured for the whole gesture, so the events keep coming once the pointer leaves for a key.
        e.Pointer.Capture(this);
        e.Handled = true;
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
        if (!IsDragging) return;

        UpdateGhost(e);
        UpdateTreeTarget(e.GetPosition(this));
    }

    private bool IsDragging => _dragDrop?.PanelDragGhost != null;

    private void OnPreviewPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_dragArmed) return;

        FolderNodeViewModel node = _pressedNode;
        bool dragged = IsDragging;
        bool overPanel = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        bool moveInTree = _hasTreeTarget && _treeTargetValid;
        FolderNodeViewModel parent = _treeTargetParent;
        int index = _treeTargetIndex;
        Point ownerPosition = ToOwner(e);

        _dragArmed = false;
        _pressedNode = null;
        HideFeedback();
        e.Pointer.Capture(null);
        e.Handled = true;

        if (!dragged)
        {
            // A click: open the folder. Never through the drag machine, whose click assigns the
            // row to the selected key.
            _dragDrop.PanelDragCancelled();
            _ = ViewModel?.OpenFolderAsync(node);
            return;
        }

        if (overPanel)
        {
            _dragDrop.PanelDragCancelled();
            if (moveInTree)
                ViewModel?.Move(node, parent, index);
            return;
        }

        _dragDrop.PanelPointerReleased(ownerPosition);
    }

    private void OnPreviewPointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        if (_dragArmed)
            EndDrag(null);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_dragArmed || e.Key != Key.Escape) return;

        EndDrag(null);
        e.Handled = true;
    }

    private void EndDrag(IPointer pointer)
    {
        _dragArmed = false;
        _pressedNode = null;
        HideFeedback();
        pointer?.Capture(null);
        _dragDrop.PanelDragCancelled();
    }

    // ── Tree drop target ───────────────────────────────────────────────────

    private void UpdateTreeTarget(Point position)
    {
        _hasTreeTarget = false;
        _dropIndicator.IsVisible = false;

        FolderPanelViewModel vm = ViewModel;
        if (vm == null || _pressedNode == null || !new Rect(Bounds.Size).Contains(position)) return;

        Visual hit = this.InputHitTest(position) as Visual;

        if (_rootRow != null && hit != null && hit.GetSelfAndVisualAncestors().Contains(_rootRow))
        {
            SetTreeTarget(null, vm.RootNodes.Count, _rootRow, into: true, above: false);
            return;
        }

        Control row = hit?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c.Classes.Contains("folder-row") && c.DataContext is FolderNodeViewModel);
        if (row?.DataContext is not FolderNodeViewModel target) return;

        Point? local = this.TranslatePoint(position, row);
        double ratio = local.HasValue && row.Bounds.Height > 0 ? local.Value.Y / row.Bounds.Height : 0.5;

        IList<FolderNodeViewModel> siblings = target.Parent?.Children ?? (IList<FolderNodeViewModel>)vm.RootNodes;
        int targetIndex = siblings.IndexOf(target);

        if (ratio < EdgeZone)
            SetTreeTarget(target.Parent, targetIndex, row, into: false, above: true);
        else if (ratio > 1 - EdgeZone)
            SetTreeTarget(target.Parent, targetIndex + 1, row, into: false, above: false);
        else
            SetTreeTarget(target, target.Children.Count, row, into: true, above: false);
    }

    private void SetTreeTarget(FolderNodeViewModel parent, int index, Control row, bool into, bool above)
    {
        _hasTreeTarget = true;
        _treeTargetParent = parent;
        _treeTargetIndex = index;
        _treeTargetValid = ViewModel.CanMove(_pressedNode, parent) && !ReferenceEquals(row.DataContext, _pressedNode);

        Point? topLeft = row.TranslatePoint(new Point(0, 0), _overlay);
        if (topLeft == null) return;

        IBrush brush = _treeTargetValid ? ValidBrush : InvalidBrush;
        Canvas.SetLeft(_dropIndicator, topLeft.Value.X);
        _dropIndicator.Width = row.Bounds.Width;

        if (into)
        {
            Canvas.SetTop(_dropIndicator, topLeft.Value.Y);
            _dropIndicator.Height = row.Bounds.Height;
            _dropIndicator.Background = Brushes.Transparent;
            _dropIndicator.BorderBrush = brush;
            _dropIndicator.BorderThickness = new Thickness(2);
        }
        else
        {
            Canvas.SetTop(_dropIndicator, topLeft.Value.Y + (above ? 0 : row.Bounds.Height) - 1);
            _dropIndicator.Height = 2;
            _dropIndicator.Background = brush;
            _dropIndicator.BorderThickness = default;
        }

        _overlay.IsVisible = true;
        _dropIndicator.IsVisible = true;
    }

    // ── Ghost ──────────────────────────────────────────────────────────────

    private void UpdateGhost(PointerEventArgs e)
    {
        IImage image = _dragDrop.PanelDragGhost;
        Point position = e.GetPosition(this);
        bool over = image != null && new Rect(Bounds.Size).Contains(position);

        if (!over)
        {
            _ghost.IsVisible = false;
            _ghostImage.Source = null;
            _overlay.IsVisible = _dropIndicator.IsVisible;
            return;
        }

        Size size = _dragDrop.PanelDragGhostSize;
        _ghostImage.Source = image;
        _ghost.Width = size.Width;
        _ghost.Height = size.Height;
        _ghost.Padding = _dragDrop.PanelDragGhostPadding;
        Canvas.SetLeft(_ghost, position.X - (size.Width / 2));
        Canvas.SetTop(_ghost, position.Y - (size.Height / 2));

        _overlay.IsVisible = true;
        _ghost.IsVisible = true;
    }

    private void HideFeedback()
    {
        _hasTreeTarget = false;
        _ghostImage.Source = null;
        _ghost.IsVisible = false;
        _dropIndicator.IsVisible = false;
        _overlay.IsVisible = false;
    }
}
