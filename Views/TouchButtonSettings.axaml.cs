using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class TouchButtonSettings : Window
{
    private enum DragMode { None, Move, Handle }

    private DragMode _dragMode = DragMode.None;
    private Point _dragStartCanvas;

    // Move-drag state
    private int _startPosX, _startPosY;

    // Device-pixel origin of the layer's display rect when its position offset is 0,
    // captured at move-drag start so snap-to-grid can align the rect's top-left edge.
    private double _moveBaseLeftDev, _moveBaseTopDev;
    private bool _moveHasBase;

    // Handle-drag state (resize / crop). signX/signY encode which edges this
    // handle moves: -1 left/top, +1 right/bottom, 0 none.
    private int _handleSignX, _handleSignY;
    private bool _isImageLayer;

    // Display rect of the layer in device-pixel space, captured at drag start.
    // This is the rectangle the renderer draws into (after Fit + Scale + Position).
    private double _startDrawX, _startDrawY, _startDstW, _startDstH;

    // Source-rect of the image layer (resolved — i.e. full image if SourceRect was Empty).
    private float _startSrcLeft, _startSrcTop, _startSrcRight, _startSrcBottom;
    private int _bmpWidth, _bmpHeight;

    private double _startScaleX, _startScaleY;
    private int _startBoxW, _startBoxH;

    // ───────── Command-chain drag state ─────────

    // Command-card currently reordered via its drag handle; null when idle.
    private CommandSegment _draggedSegment;
    private Point _segmentDragStart;
    private bool _segmentDragging;

    // Tree node armed for a drag-to-insert; promoted to a real drag once the pointer
    // moves past the threshold (so a click/double-click is not swallowed). We use the
    // same pointer-capture approach as the card reorder rather than the OS drag loop,
    // which keeps double-click-to-append working.
    private MenuEntry _treeDragCandidate;
    private Point _treeDragStart;
    private bool _treeDragging;

    public TouchButtonSettings()
    {
        InitializeComponent();

        // Re-evaluate which chips start a wrapped row (and thus shouldn't draw a
        // leading arrow) after every layout pass — covers adds, removes, reorders
        // and window/strip resizes that re-wrap the sequence.
        CommandList.LayoutUpdated += (_, _) => UpdateConnectorVisibility();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }

            if (DataContext is TouchButtonSettingsViewModel tbvm)
            {
                tbvm.Cleanup();
            }
        };
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TouchButtonSettingsViewModel vm) return;

        var confirmed = await ConfirmDialogHelper.AskYesNoAsync(
            this,
            "Reset Button",
            "Do you really want to reset this button? All settings, texts, images and the command will be lost.");

        if (!confirmed) return;

        Closed += (_, _) => vm.ClearButton();
        Close();
    }

    // ───────── Command chain: remove / reorder / drag-insert ─────────

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommandSegment segment } &&
            DataContext is TouchButtonSettingsViewModel vm)
            vm.RemoveSegment(segment);
    }

    // Live reorder of chain cards — mirrors the macro editor: the drag handle
    // captures the pointer onto the (stable) ItemsControl, then every move maps
    // the pointer to a target index and moves the card there immediately.

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
            DataContext is TouchButtonSettingsViewModel vm)
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
    // card reorder: arm on press, promote to a drag past a small threshold, then track
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
            DataContext is TouchButtonSettingsViewModel vm && IsOverDropZone(e))
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

    private async void ChangeSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TouchButtonSettingsViewModel vm)
            await vm.ChangeSelectedSymbolAsync();
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
            DataContext is TouchButtonSettingsViewModel vm)
        {
            vm.InsertCommand(menuEntry);
            _treeDragCandidate = null;
            e.Handled = true;
        }
    }

    private void Editor_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TouchButtonSettingsViewModel vm || vm.ButtonData == null) return;

        var pos = e.GetPosition(EditorRoot);

        // 1) Handle hit-test — only meaningful for layers that show handles
        //    (suppressed for text layers via ScaleHandlesVisible).
        if (vm.ScaleHandlesVisible && e.Source is Rectangle r && r.Tag is string tag &&
            TryParseHandleSign(tag, out var sx, out var sy))
        {
            BeginHandleDrag(vm, vm.SelectedLayer, sx, sy, pos);
            e.Handled = true;
            return;
        }

        // 2) Selection-rect hit (Move) or layer body hit (Select + Move)
        var picked = vm.SelectedLayer != null &&
                     IsInside(pos, vm.SelectionLeft, vm.SelectionTop, vm.SelectionWidth, vm.SelectionHeight)
            ? vm.SelectedLayer
            : HitTestLayer(vm, pos);

        if (picked != null)
        {
            if (!ReferenceEquals(picked, vm.SelectedLayer))
                vm.SelectedLayer = picked;

            BeginMoveDrag(vm, picked, pos);
            e.Handled = true;
            return;
        }

        // 3) Nothing hit → deselect
        vm.SelectedLayer = null;
    }

    private void Editor_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None) return;
        if (DataContext is not TouchButtonSettingsViewModel vm || vm.SelectedLayer == null) return;

        var pos = e.GetPosition(EditorRoot);
        var canvasToDevice = 1.0 / TouchButtonSettingsViewModel.EditorToDeviceScale;

        switch (_dragMode)
        {
            case DragMode.Move:
            {
                var dxDev = (int)Math.Round((pos.X - _dragStartCanvas.X) * canvasToDevice);
                var dyDev = (int)Math.Round((pos.Y - _dragStartCanvas.Y) * canvasToDevice);
                var newPosX = _startPosX + dxDev;
                var newPosY = _startPosY + dyDev;

                if (vm.SnapToGrid && _moveHasBase)
                {
                    const int step = TouchButtonSettingsViewModel.GridStepDevice;
                    var snappedLeft = Math.Round((_moveBaseLeftDev + newPosX) / step) * step;
                    var snappedTop = Math.Round((_moveBaseTopDev + newPosY) / step) * step;
                    newPosX = (int)Math.Round(snappedLeft - _moveBaseLeftDev);
                    newPosY = (int)Math.Round(snappedTop - _moveBaseTopDev);
                }

                vm.SelectedLayer.PositionX = newPosX;
                vm.SelectedLayer.PositionY = newPosY;
                break;
            }
            case DragMode.Handle:
            {
                var altHeld   = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
                var shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                // In device-pixel space:
                var dxDev = (pos.X - _dragStartCanvas.X) * canvasToDevice;
                var dyDev = (pos.Y - _dragStartCanvas.Y) * canvasToDevice;

                // Snap the dragged edge/corner to the grid. The active edge moves by
                // exactly dxDev/dyDev from its start position (true for both resize and
                // crop), so snapping the edge reduces to snapping the delta.
                if (vm.SnapToGrid)
                {
                    const int step = TouchButtonSettingsViewModel.GridStepDevice;
                    if (_handleSignX != 0)
                    {
                        var startEdgeX = _startDrawX + (_handleSignX > 0 ? _startDstW : 0);
                        dxDev = Math.Round((startEdgeX + dxDev) / step) * step - startEdgeX;
                    }
                    if (_handleSignY != 0)
                    {
                        var startEdgeY = _startDrawY + (_handleSignY > 0 ? _startDstH : 0);
                        dyDev = Math.Round((startEdgeY + dyDev) / step) * step - startEdgeY;
                    }
                }

                if (altHeld && _isImageLayer && vm.SelectedLayer is ImageLayer img)
                    ApplyCrop(img, dxDev, dyDev);
                else
                    ApplyResize(vm.SelectedLayer, dxDev, dyDev, shiftHeld);
                break;
            }
        }

        vm.PreviewRefreshDuringDrag();
        e.Handled = true;
    }

    private void Editor_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_dragMode == DragMode.None) return;
        _dragMode = DragMode.None;

        if (DataContext is TouchButtonSettingsViewModel vm && vm.ButtonData != null)
        {
            vm.ButtonData.IgnoreRefresh = false;
            vm.ButtonData.Refresh();
        }

        e.Handled = true;
    }

    private void BeginMoveDrag(TouchButtonSettingsViewModel vm, LayerBase layer, Point pos)
    {
        _dragMode = DragMode.Move;
        _dragStartCanvas = pos;
        _startPosX = layer.PositionX;
        _startPosY = layer.PositionY;

        // Capture the rect's device-space origin minus the current position offset, so
        // snap-to-grid can compute the rect's edge from any candidate position offset.
        var bounds = BitmapHelper.GetLayerEditorBounds(
            layer,
            TouchButtonSettingsViewModel.EditorCanvasSize,
            TouchButtonSettingsViewModel.EditorFrameSize);
        if (bounds is { } b)
        {
            var scale = TouchButtonSettingsViewModel.EditorToDeviceScale;
            var frameOffset = (TouchButtonSettingsViewModel.EditorCanvasSize -
                               TouchButtonSettingsViewModel.EditorFrameSize) / 2.0;
            _moveBaseLeftDev = (b.Left - frameOffset) / scale - _startPosX;
            _moveBaseTopDev = (b.Top - frameOffset) / scale - _startPosY;
            _moveHasBase = true;
        }
        else
        {
            _moveHasBase = false;
        }

        vm.ButtonData.IgnoreRefresh = true;
    }

    private void BeginHandleDrag(TouchButtonSettingsViewModel vm, LayerBase layer,
        int signX, int signY, Point pos)
    {
        _dragMode = DragMode.Handle;
        _dragStartCanvas = pos;
        _handleSignX = signX;
        _handleSignY = signY;
        _isImageLayer = layer is ImageLayer;

        _startPosX = layer.PositionX;
        _startPosY = layer.PositionY;
        _startScaleX = layer.EffectiveScaleX;
        _startScaleY = layer.EffectiveScaleY;

        // Compute the layer's current display rect in device-pixel space.
        // For image layers we also resolve the effective source rect so the
        // crop math operates on absolute source pixel coordinates.
        const int deviceSize = TouchButtonSettingsViewModel.DeviceSize;
        if (layer is ImageLayer img && img.CachedImage != null)
        {
            _bmpWidth = img.CachedImage.Width;
            _bmpHeight = img.CachedImage.Height;

            if (!img.SourceRect.IsEmpty && img.SourceRect.Width > 0 && img.SourceRect.Height > 0)
            {
                _startSrcLeft = img.SourceRect.Left;
                _startSrcTop = img.SourceRect.Top;
                _startSrcRight = img.SourceRect.Right;
                _startSrcBottom = img.SourceRect.Bottom;
            }
            else
            {
                _startSrcLeft = 0;
                _startSrcTop = 0;
                _startSrcRight = _bmpWidth;
                _startSrcBottom = _bmpHeight;
            }

            var srcW = _startSrcRight - _startSrcLeft;
            var srcH = _startSrcBottom - _startSrcTop;
            var fit = Math.Min(deviceSize / srcW, deviceSize / srcH);
            _startDstW = srcW * fit * _startScaleX;
            _startDstH = srcH * fit * _startScaleY;
            _startDrawX = (deviceSize - _startDstW) / 2.0 + _startPosX;
            _startDrawY = (deviceSize - _startDstH) / 2.0 + _startPosY;
        }
        else if (layer is TextLayer text)
        {
            _startBoxW = text.EffectiveBoxWidth;
            _startBoxH = text.EffectiveBoxHeight;
            _startDstW = _startBoxW;
            _startDstH = _startBoxH;
            if (text.Centered)
            {
                _startDrawX = (deviceSize - _startBoxW) / 2.0 + text.PositionX;
                _startDrawY = (deviceSize - _startBoxH) / 2.0 + text.PositionY;
            }
            else
            {
                _startDrawX = text.PositionX;
                _startDrawY = text.PositionY;
            }
        }
        else
        {
            // Symbol or other future layer: use the selection bounds.
            var canvasToDevice = 1.0 / TouchButtonSettingsViewModel.EditorToDeviceScale;
            var frameOffset = (TouchButtonSettingsViewModel.EditorCanvasSize -
                               TouchButtonSettingsViewModel.EditorFrameSize) / 2.0;
            _startDstW = vm.SelectionWidth * canvasToDevice;
            _startDstH = vm.SelectionHeight * canvasToDevice;
            _startDrawX = (vm.SelectionLeft - frameOffset) * canvasToDevice;
            _startDrawY = (vm.SelectionTop - frameOffset) * canvasToDevice;
        }

        vm.ButtonData.IgnoreRefresh = true;
    }

    /// <summary>
    /// OBS-style resize:
    /// <list type="bullet">
    /// <item>Edge handles (N/S/E/W) always change only their own axis — the
    /// orthogonal axis is never touched, so aspect can change naturally.</item>
    /// <item>Corner handles preserve aspect by default: the drag delta is
    /// projected onto the line through the pivot in the original handle
    /// direction, so the corner travels along the box's diagonal regardless
    /// of which way the mouse moves.</item>
    /// <item>Holding Shift on a corner unlocks the aspect ratio so X and Y
    /// follow the pointer independently.</item>
    /// </list>
    /// The pivot is the opposite corner/edge midpoint and stays pinned in
    /// device-pixel space.
    /// </summary>
    private void ApplyResize(LayerBase layer, double dxDev, double dyDev, bool shiftHeld)
    {
        if (_startDstW <= 0 || _startDstH <= 0) return;

        double factorX, factorY;

        if (_handleSignX != 0 && _handleSignY != 0 && !shiftHeld)
        {
            // Corner + aspect lock: project pointer drag onto the original
            // handle vector. Same factor on both axes ⇒ aspect preserved.
            var hvx = _handleSignX * _startDstW;
            var hvy = _handleSignY * _startDstH;
            var l2 = hvx * hvx + hvy * hvy;
            if (l2 <= 0) return;
            var l = Math.Sqrt(l2);
            var proj = l + (dxDev * hvx + dyDev * hvy) / l;
            var factor = proj / l;
            factorX = factor;
            factorY = factor;
        }
        else
        {
            // Edge handle, or shift-held corner: per-axis, independent.
            factorX = _handleSignX == 0 ? 1.0 : (_startDstW + _handleSignX * dxDev) / _startDstW;
            factorY = _handleSignY == 0 ? 1.0 : (_startDstH + _handleSignY * dyDev) / _startDstH;
        }

        factorX = Math.Max(0.05, factorX);
        factorY = Math.Max(0.05, factorY);

        double finalDstW, finalDstH;
        if (layer is TextLayer text)
        {
            // Text layers don't scale the glyphs — they resize their layout box
            // and re-wrap inside. The handles drive BoxWidth/Height directly.
            var newBoxW = Math.Max(1, (int)Math.Round(_startBoxW * factorX));
            var newBoxH = Math.Max(1, (int)Math.Round(_startBoxH * factorY));
            text.BoxWidth = newBoxW;
            text.BoxHeight = newBoxH;
            finalDstW = newBoxW;
            finalDstH = newBoxH;
        }
        else
        {
            var newScaleX = Math.Clamp(_startScaleX * factorX, 0.05, 20.0);
            var newScaleY = Math.Clamp(_startScaleY * factorY, 0.05, 20.0);
            layer.Scale = newScaleX;
            layer.ScaleY = newScaleY;
            finalDstW = (newScaleX / Math.Max(1e-6, _startScaleX)) * _startDstW;
            finalDstH = (newScaleY / Math.Max(1e-6, _startScaleY)) * _startDstH;
        }

        // Reposition so the pivot (side opposite to the handle) stays put.
        UpdatePositionForResize(layer, finalDstW, finalDstH);
    }

    /// <summary>
    /// Alt-drag handles: change the image layer's <see cref="ImageLayer.SourceRect"/>
    /// such that the dragged side of the display rect tracks the pointer while
    /// the opposite side stays pinned. Scale and position are recomputed so the
    /// renderer outputs exactly that display rect.
    /// </summary>
    private void ApplyCrop(ImageLayer img, double dxDev, double dyDev)
    {
        if (_startDstW <= 0 || _startDstH <= 0) return;
        if (_bmpWidth <= 0 || _bmpHeight <= 0) return;

        var startSrcW = _startSrcRight - _startSrcLeft;
        var startSrcH = _startSrcBottom - _startSrcTop;
        if (startSrcW <= 0 || startSrcH <= 0) return;

        // 1) New display rect size for this drag.
        var newDstW = _startDstW + _handleSignX * dxDev;
        var newDstH = _startDstH + _handleSignY * dyDev;
        if (_handleSignX == 0) newDstW = _startDstW;
        if (_handleSignY == 0) newDstH = _startDstH;

        // Minimum 2 device pixels so the result stays grabable.
        newDstW = Math.Max(2.0, newDstW);
        newDstH = Math.Max(2.0, newDstH);
        // Don't allow the crop to "grow" the image past the original frame.
        newDstW = Math.Min(_startDstW, newDstW);
        newDstH = Math.Min(_startDstH, newDstH);

        // 2) New source rect proportional to the displayed shrink.
        var newSrcW = startSrcW * (newDstW / _startDstW);
        var newSrcH = startSrcH * (newDstH / _startDstH);
        var cropX = startSrcW - newSrcW;
        var cropY = startSrcH - newSrcH;

        float newSrcLeft = _startSrcLeft, newSrcRight = _startSrcRight;
        float newSrcTop = _startSrcTop, newSrcBottom = _startSrcBottom;

        if (_handleSignX > 0) newSrcRight = _startSrcRight - (float)cropX;
        else if (_handleSignX < 0) newSrcLeft = _startSrcLeft + (float)cropX;

        if (_handleSignY > 0) newSrcBottom = _startSrcBottom - (float)cropY;
        else if (_handleSignY < 0) newSrcTop = _startSrcTop + (float)cropY;

        newSrcLeft = Math.Clamp(newSrcLeft, 0, _bmpWidth);
        newSrcRight = Math.Clamp(newSrcRight, 0, _bmpWidth);
        newSrcTop = Math.Clamp(newSrcTop, 0, _bmpHeight);
        newSrcBottom = Math.Clamp(newSrcBottom, 0, _bmpHeight);

        if (newSrcRight - newSrcLeft < 1 || newSrcBottom - newSrcTop < 1) return;

        // 3) Recompute scale + position so the renderer reproduces the desired
        //    display rect after re-fitting the cropped source.
        const int deviceSize = TouchButtonSettingsViewModel.DeviceSize;
        var actualSrcW = newSrcRight - newSrcLeft;
        var actualSrcH = newSrcBottom - newSrcTop;
        var newFit = Math.Min(deviceSize / actualSrcW, deviceSize / actualSrcH);

        var newScaleX = newDstW / (actualSrcW * newFit);
        var newScaleY = newDstH / (actualSrcH * newFit);

        img.SourceRect = new SerializableRect(newSrcLeft, newSrcTop, newSrcRight, newSrcBottom);
        img.Scale = newScaleX;
        img.ScaleY = newScaleY;

        UpdatePositionForResize(img, newDstW, newDstH);
    }

    /// <summary>
    /// Recomputes <see cref="LayerBase.PositionX"/>/Y so the layer's display rect
    /// keeps the pivot edge/corner (the side opposite to the dragged handle)
    /// anchored at its drag-start device-pixel coordinates.
    /// </summary>
    private void UpdatePositionForResize(LayerBase layer, double newDstW, double newDstH)
    {
        const int deviceSize = TouchButtonSettingsViewModel.DeviceSize;

        // Pivot side fractions inside the rect: 0 = left/top, 1 = right/bottom, 0.5 = centered.
        double pivotFracX = _handleSignX switch { +1 => 0.0, -1 => 1.0, _ => 0.5 };
        double pivotFracY = _handleSignY switch { +1 => 0.0, -1 => 1.0, _ => 0.5 };

        var pivotXDev = _startDrawX + pivotFracX * _startDstW;
        var pivotYDev = _startDrawY + pivotFracY * _startDstH;

        var newDrawX = pivotXDev - pivotFracX * newDstW;
        var newDrawY = pivotYDev - pivotFracY * newDstH;

        // Non-centered text stores PositionX/Y as the absolute box top-left.
        // Everything else (images, symbols, centered text) stores them as the
        // offset from the device-center of the bounding rect.
        if (layer is TextLayer { Centered: false })
        {
            layer.PositionX = (int)Math.Round(newDrawX);
            layer.PositionY = (int)Math.Round(newDrawY);
        }
        else
        {
            layer.PositionX = (int)Math.Round(newDrawX - (deviceSize - newDstW) / 2.0);
            layer.PositionY = (int)Math.Round(newDrawY - (deviceSize - newDstH) / 2.0);
        }
    }

    private static bool TryParseHandleSign(string tag, out int signX, out int signY)
    {
        switch (tag)
        {
            case "NW": signX = -1; signY = -1; return true;
            case "N":  signX =  0; signY = -1; return true;
            case "NE": signX =  1; signY = -1; return true;
            case "W":  signX = -1; signY =  0; return true;
            case "E":  signX =  1; signY =  0; return true;
            case "SW": signX = -1; signY =  1; return true;
            case "S":  signX =  0; signY =  1; return true;
            case "SE": signX =  1; signY =  1; return true;
            default:   signX =  0; signY =  0; return false;
        }
    }

    /// <summary>
    /// Topmost-first hit test against the on-canvas bounds of every layer.
    /// Returns null if the pointer is over empty canvas.
    /// </summary>
    private static LayerBase HitTestLayer(TouchButtonSettingsViewModel vm, Point pos)
    {
        if (vm.ButtonData?.Layers == null) return null;

        for (var i = vm.ButtonData.Layers.Count - 1; i >= 0; i--)
        {
            var layer = vm.ButtonData.Layers[i];
            if (layer == null || !layer.Visible) continue;

            var rect = BitmapHelper.GetLayerEditorBounds(
                layer,
                TouchButtonSettingsViewModel.EditorCanvasSize,
                TouchButtonSettingsViewModel.EditorFrameSize);

            if (rect == null) continue;

            if (IsInside(pos, rect.Value.Left, rect.Value.Top, rect.Value.Width, rect.Value.Height))
                return layer;
        }

        return null;
    }

    private static bool IsInside(Point p, double x, double y, double w, double h)
        => p.X >= x && p.X <= x + w && p.Y >= y && p.Y <= y + h;
}
