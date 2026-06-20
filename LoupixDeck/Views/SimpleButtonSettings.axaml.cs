using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class SimpleButtonSettings : Window
{
    // ───────── Command-chain drag state ─────────

    // Command-chip currently reordered via its drag handle; null when idle.
    private CommandSegment _draggedSegment;
    private Point _segmentDragStart;
    private bool _segmentDragging;

    // Tree node armed for a drag-to-insert; promoted to a real drag once the pointer
    // moves past the threshold (so a click/double-click is not swallowed). We use the
    // same pointer-capture approach as the chip reorder rather than the OS drag loop,
    // which keeps double-click-to-append working.
    private MenuEntry _treeDragCandidate;
    private Point _treeDragStart;
    private bool _treeDragging;

    public SimpleButtonSettings()
    {
        InitializeComponent();

        // Re-evaluate which chips start a wrapped row (and thus shouldn't draw a
        // leading arrow) after every layout pass — covers adds, removes, reorders
        // and resizes that re-wrap the sequence.
        CommandList.LayoutUpdated += (_, _) => UpdateConnectorVisibility();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }

            if (DataContext is SimpleButtonSettingsViewModel sbvm)
            {
                sbvm.Cleanup();
            }
        };
    }

    // ───────── Command chain: remove / reorder / drag-insert ─────────

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommandSegment segment } &&
            DataContext is SimpleButtonSettingsViewModel vm)
            vm.RemoveSegment(segment);
    }

    // Live reorder of chain chips — the drag handle captures the pointer onto the
    // (stable) ItemsControl, then every move maps the pointer to a target index and
    // moves the chip there immediately.

    private void CommandDragHandle_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandSegment segment })
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Arm the reorder; the dim + ghost + insertion marker only appear once
        // the pointer moves past the threshold (CommandList_PointerMoved), so a
        // plain click on the grip doesn't flash any drag chrome.
        _draggedSegment = segment;
        _segmentDragStart = e.GetPosition(this);
        _segmentDragging = false;
        e.Pointer.Capture(CommandList);
        e.Handled = true;
    }

    private void CommandList_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_draggedSegment == null)
            return;

        if (!_segmentDragging)
        {
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _segmentDragStart.X) < 4 && Math.Abs(p.Y - _segmentDragStart.Y) < 4)
                return;

            _segmentDragging = true;
            _draggedSegment.IsDragging = true;
            ShowDragGhost(_draggedSegment.DisplayName);
        }

        UpdateDragGhost(e);
        UpdateDropMarker(FindDropIndex(e));
    }

    private void CommandList_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_segmentDragging && _draggedSegment != null &&
            DataContext is SimpleButtonSettingsViewModel vm)
        {
            var from = vm.Commands.IndexOf(_draggedSegment);
            if (from >= 0)
            {
                // Drop index counts slots in the full list; account for the
                // dragged item being removed from before the target first.
                var dropIndex = FindDropIndex(e);
                var to = dropIndex > from ? dropIndex - 1 : dropIndex;
                vm.MoveSegment(from, to);
            }
        }

        EndCommandDrag(e.Pointer);
    }

    private void CommandList_PointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
        => EndCommandDrag(null);

    private void EndCommandDrag(IPointer pointer)
    {
        if (_draggedSegment == null)
            return;

        _draggedSegment.IsDragging = false;
        _draggedSegment = null;
        _segmentDragging = false;
        HideDragGhost();
        HideDropMarker();
        pointer?.Capture(null);
    }

    /// <summary>Insertion index (0..count) for the pointer, in reading order over
    /// the wrapped chip rows: a chip counts as "after" the pointer if it sits on a
    /// lower row, or on the same row past the pointer's X. The first such chip is
    /// the slot; if none, the drop appends at the end.</summary>
    private int FindDropIndex(PointerEventArgs e)
    {
        var count = CommandList.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (CommandList.ContainerFromIndex(i) is not Control container)
                continue;

            var p = e.GetPosition(container);
            // Pointer is on a row above this chip → insert before it.
            if (p.Y < 0)
                return i;
            // Pointer is within this chip's row, left of its midpoint.
            if (p.Y <= container.Bounds.Height && p.X < container.Bounds.Width / 2)
                return i;
        }

        return count;
    }

    /// <summary>Places the vertical insertion bar at the left edge of the chip at
    /// <paramref name="dropIndex"/> (or the right edge of the last chip when
    /// appending), translated into the overlay canvas.</summary>
    private void UpdateDropMarker(int dropIndex)
    {
        var count = CommandList.ItemCount;
        if (count == 0)
        {
            HideDropMarker();
            return;
        }

        var trailing = dropIndex >= count;
        var index = trailing ? count - 1 : dropIndex;
        if (CommandList.ContainerFromIndex(index) is not Control container)
        {
            HideDropMarker();
            return;
        }

        var edgeX = trailing ? container.Bounds.Width : 0.0;
        var origin = container.TranslatePoint(new Point(edgeX, 0), DragGhostLayer);
        if (origin == null)
        {
            HideDropMarker();
            return;
        }

        DropMarker.Height = container.Bounds.Height;
        Canvas.SetLeft(DropMarker, origin.Value.X - 1.5);
        Canvas.SetTop(DropMarker, origin.Value.Y);
        DropMarker.IsVisible = true;
    }

    private void HideDropMarker() => DropMarker.IsVisible = false;

    /// <summary>
    /// Hides the leading connector arrow on the first chip of each wrapped row, so
    /// it never dangles at a row's left edge. The arrow's gutter has a fixed width,
    /// so toggling the glyph never changes how the chips pack — row assignments stay
    /// stable and this converges instead of oscillating.
    /// </summary>
    private void UpdateConnectorVisibility()
    {
        var count = CommandList.ItemCount;
        var prevY = double.NaN;

        for (var i = 0; i < count; i++)
        {
            if (CommandList.ContainerFromIndex(i) is not Control container)
                continue;

            // Chips on the same row share a top (Y); a larger Y means a new row.
            var y = container.Bounds.Y;
            var rowStart = double.IsNaN(prevY) || y > prevY + 0.5;
            if (container.DataContext is CommandSegment segment)
                segment.ShowConnector = i > 0 && !rowStart;

            prevY = y;
        }
    }

    // Drag a command from the tree into the chain at a specific position. Mirrors the
    // chip reorder: arm on press, promote to a drag past a small threshold, then track
    // the pointer (captured on the tree) and insert on release when over the chain.

    private void SystemCommandsTree_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_treeDragCandidate == null)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            EndTreeDrag(e.Pointer);
            return;
        }

        var pos = point.Position;
        if (!_treeDragging)
        {
            if (Math.Abs(pos.X - _treeDragStart.X) < 4 && Math.Abs(pos.Y - _treeDragStart.Y) < 4)
                return;

            _treeDragging = true;
            e.Pointer.Capture(SystemCommandsTreeView);
            ShowDragGhost(_treeDragCandidate?.Name);
        }

        UpdateDragGhost(e);

        var over = IsOverDropZone(e);
        CommandDropZone.Classes.Set("drop-active", over);
        if (over)
            UpdateDropMarker(FindDropIndex(e));
        else
            HideDropMarker();
    }

    private void ShowDragGhost(string label)
    {
        DragGhostText.Text = label ?? string.Empty;
        DragGhostLayer.IsVisible = true;
    }

    private void UpdateDragGhost(PointerEventArgs e)
    {
        if (!DragGhostLayer.IsVisible)
            return;

        // Offset from the cursor so the ghost doesn't sit under the pointer.
        var pos = e.GetPosition(DragGhostLayer);
        Canvas.SetLeft(DragGhost, pos.X + 14);
        Canvas.SetTop(DragGhost, pos.Y + 10);
    }

    private void HideDragGhost() => DragGhostLayer.IsVisible = false;

    private void SystemCommandsTree_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_treeDragging && _treeDragCandidate != null &&
            DataContext is SimpleButtonSettingsViewModel vm && IsOverDropZone(e))
        {
            vm.InsertCommandAt(_treeDragCandidate, FindDropIndex(e));
        }

        EndTreeDrag(e.Pointer);
    }

    private void SystemCommandsTree_PointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
        => EndTreeDrag(null);

    private void EndTreeDrag(IPointer pointer)
    {
        _treeDragCandidate = null;
        _treeDragging = false;
        HideDragGhost();
        HideDropMarker();
        CommandDropZone.Classes.Set("drop-active", false);
        pointer?.Capture(null);
    }

    private bool IsOverDropZone(PointerEventArgs e)
    {
        var pos = e.GetPosition(CommandDropZone);
        return pos.X >= 0 && pos.Y >= 0 &&
               pos.X <= CommandDropZone.Bounds.Width &&
               pos.Y <= CommandDropZone.Bounds.Height;
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // The handler sits on the row's full-width root container, so its
        // DataContext is the entry regardless of where in the row the click lands.
        if (sender is not Control { DataContext: MenuEntry menuEntry })
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (!string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            // Command leaf: arm a possible drag-to-insert; SystemCommandsTree_PointerMoved
            // promotes it to a real drag once the pointer moves far enough. Double-click-
            // to-append is handled by OnCommandDoubleTapped.
            _treeDragCandidate = menuEntry;
            _treeDragStart = e.GetPosition(this);
        }
        else if ((sender as Control).FindAncestorOfType<TreeViewItem>() is { } treeViewItem)
        {
            // Group node: expand/collapse on a click anywhere along the row.
            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
            e.Handled = true;
        }
    }

    // Append a command on double-click, driven by Avalonia's DoubleTapped gesture.
    // The gesture recognizer handles the platform double-click time and a small
    // movement tolerance itself, and re-arms after each pair — so the same command
    // can be added repeatedly without jiggling the mouse, and a little jitter
    // between the two clicks no longer cancels the double-click.
    private void OnCommandDoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MenuEntry menuEntry } &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command) &&
            DataContext is SimpleButtonSettingsViewModel vm)
        {
            vm.InsertCommand(menuEntry);
            _treeDragCandidate = null;
            e.Handled = true;
        }
    }
}
