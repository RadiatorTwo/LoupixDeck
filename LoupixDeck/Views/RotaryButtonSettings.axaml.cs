using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class RotaryButtonSettings : Window
{
    // The three sequence strips, paired with their chip list. Resolved once after
    // InitializeComponent; the owning slot is read from each list's DataContext at
    // use time (it is null until the view model has run Initialize).
    private (Border Zone, ItemsControl List)[] _strips;

    // ───────── Chip-reorder drag state ─────────

    private CommandSegment _draggedSegment;
    private ItemsControl _dragList;
    private CommandSequenceSlot _dragSlot;
    private Point _segmentDragStart;
    private bool _segmentDragging;

    // ───────── Picker drag-to-insert state ─────────

    // The command being dragged from the picker; null when idle. The picker owns the
    // pointer capture and threshold; this window positions the ghost / per-strip marker.
    private MenuEntry _pickerDragEntry;

    public RotaryButtonSettings()
    {
        InitializeComponent();

        _strips =
        [
            (CommandDropZoneLeft, CommandListLeft),
            (CommandDropZoneRight, CommandListRight),
            (CommandDropZonePress, CommandListPress),
        ];

        // Re-evaluate which chips start a wrapped row (and thus shouldn't draw a
        // leading arrow) after every layout pass, for each strip independently.
        foreach (var (_, list) in _strips)
            list.LayoutUpdated += (_, _) => UpdateConnectorVisibility(list);

        // Card-based command picker → sequence-strip assignment (issue #171).
        PickerControl.CommandActivated += Picker_CommandActivated;
        PickerControl.CommandDragStarted += Picker_CommandDragStarted;
        PickerControl.CommandDragMoved += Picker_CommandDragMoved;
        PickerControl.CommandDragReleased += Picker_CommandDragReleased;

        // Clicking anywhere inside a strip makes it the double-click target. Tunnel
        // so it fires even when a child (button, chip grip) handles the press itself.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel);

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }

            if (DataContext is RotaryButtonSettingsViewModel rbvm)
            {
                rbvm.Cleanup();
            }
        };
    }

    // ───────── Active-slot selection ─────────

    private void OnAnyPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RotaryButtonSettingsViewModel vm)
            return;

        // Walk up from the click target to the nearest control bound to a slot.
        var visual = e.Source as Visual;
        while (visual != null)
        {
            if (visual is Control { DataContext: CommandSequenceSlot slot })
            {
                vm.SetActiveSlot(slot);
                return;
            }

            visual = visual.GetVisualParent();
        }
    }

    // ───────── Chip chain: remove / reorder ─────────

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommandSegment segment } button &&
            ResolveSlot(button) is { } slot)
        {
            slot.RemoveSegment(segment);
        }
    }

    private void CommandDragHandle_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandSegment segment } control)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var list = control.FindAncestorOfType<ItemsControl>();
        if (list == null)
            return;

        _draggedSegment = segment;
        _dragList = list;
        _dragSlot = list.DataContext as CommandSequenceSlot;
        _segmentDragStart = e.GetPosition(this);
        _segmentDragging = false;
        e.Pointer.Capture(list);
        e.Handled = true;
    }

    private void CommandList_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_draggedSegment == null || _dragList == null)
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
        UpdateDropMarker(_dragList, FindDropIndex(_dragList, e));
    }

    private void CommandList_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_segmentDragging && _draggedSegment != null && _dragList != null && _dragSlot != null)
        {
            var from = _dragSlot.Commands.IndexOf(_draggedSegment);
            if (from >= 0)
            {
                var dropIndex = FindDropIndex(_dragList, e);
                var to = dropIndex > from ? dropIndex - 1 : dropIndex;
                _dragSlot.MoveSegment(from, to);
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
        _dragList = null;
        _dragSlot = null;
        _segmentDragging = false;
        HideDragGhost();
        HideDropMarker();
        pointer?.Capture(null);
    }

    /// <summary>Insertion index (0..count) for the pointer over <paramref name="list"/>,
    /// in reading order over the wrapped chip rows: a chip counts as "after" the pointer
    /// if it sits on a lower row, or on the same row past its midpoint. The first such
    /// chip is the slot; if none, the drop appends at the end.</summary>
    private static int FindDropIndex(ItemsControl list, PointerEventArgs e)
    {
        var count = list.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;

            var p = e.GetPosition(container);
            if (p.Y < 0)
                return i;
            if (p.Y <= container.Bounds.Height && p.X < container.Bounds.Width / 2)
                return i;
        }

        return count;
    }

    /// <summary>Places the vertical insertion bar at the left edge of the chip at
    /// <paramref name="dropIndex"/> (or the right edge of the last chip when appending)
    /// in <paramref name="list"/>, translated into the overlay canvas.</summary>
    private void UpdateDropMarker(ItemsControl list, int dropIndex)
    {
        var count = list.ItemCount;
        if (count == 0)
        {
            HideDropMarker();
            return;
        }

        var trailing = dropIndex >= count;
        var index = trailing ? count - 1 : dropIndex;
        if (list.ContainerFromIndex(index) is not Control container)
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
    /// Hides the leading connector arrow on the first chip of each wrapped row of
    /// <paramref name="list"/>, so it never dangles at a row's left edge. The arrow's
    /// gutter has a fixed width, so toggling the glyph never re-packs the chips —
    /// row assignments stay stable and this converges instead of oscillating.
    /// </summary>
    private static void UpdateConnectorVisibility(ItemsControl list)
    {
        var count = list.ItemCount;
        var prevY = double.NaN;

        for (var i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not Control container)
                continue;

            var y = container.Bounds.Y;
            var rowStart = double.IsNaN(prevY) || y > prevY + 0.5;
            if (container.DataContext is CommandSegment segment)
                segment.ShowConnector = i > 0 && !rowStart;

            prevY = y;
        }
    }

    // ───────── Picker drag-to-insert ─────────
    // The picker owns the pointer capture and threshold; this window tracks the ghost
    // and per-strip drop marker and inserts on release over a strip. A rotary command
    // group fills all three slots; a plain command lands in the strip under the pointer.

    private void Picker_CommandActivated(object sender, MenuEntry entry)
    {
        // A group fills all slots; a plain command appends to the active slot. The
        // view model routes on the entry type.
        if (entry != null && DataContext is RotaryButtonSettingsViewModel vm)
            vm.InsertCommand(entry);
    }

    private void Picker_CommandDragStarted(object sender, CommandDragEventArgs e)
    {
        _pickerDragEntry = e.Entry;
        ShowDragGhost(e.Entry?.Name);
        Picker_CommandDragMoved(sender, e);
    }

    private void Picker_CommandDragMoved(object sender, CommandDragEventArgs e)
    {
        if (_pickerDragEntry == null)
            return;

        UpdateDragGhost(e.Pointer);

        var target = StripUnderPointer(e.Pointer);

        if (_pickerDragEntry.RotaryGroup is { Count: > 0 })
        {
            // A group fills every slot at once: highlight all strips when over the
            // drop area and show no per-slot insertion marker.
            foreach (var (zone, _) in _strips)
                zone.Classes.Set("drop-active", target.HasValue);
            HideDropMarker();
            return;
        }

        foreach (var (zone, _) in _strips)
            zone.Classes.Set("drop-active", target.HasValue && ReferenceEquals(zone, target.Value.Zone));

        if (target.HasValue)
            UpdateDropMarker(target.Value.List, FindDropIndex(target.Value.List, e.Pointer));
        else
            HideDropMarker();
    }

    private void Picker_CommandDragReleased(object sender, CommandDragEventArgs e)
    {
        if (_pickerDragEntry != null && StripUnderPointer(e.Pointer) is { } target &&
            target.List.DataContext is CommandSequenceSlot slot)
        {
            if (_pickerDragEntry.RotaryGroup is { Count: > 0 } &&
                DataContext is RotaryButtonSettingsViewModel vm)
            {
                // Dropping a group anywhere on the strips applies the whole mapping.
                vm.InsertCommand(_pickerDragEntry);
            }
            else
            {
                slot.InsertCommandAt(_pickerDragEntry, FindDropIndex(target.List, e.Pointer));
            }
        }

        _pickerDragEntry = null;
        HideDragGhost();
        HideDropMarker();
        foreach (var (zone, _) in _strips)
            zone.Classes.Set("drop-active", false);
    }

    // ───────── Drag ghost ─────────

    private void ShowDragGhost(string label)
    {
        DragGhostText.Text = label ?? string.Empty;
        DragGhostLayer.IsVisible = true;
    }

    private void UpdateDragGhost(PointerEventArgs e)
    {
        if (!DragGhostLayer.IsVisible)
            return;

        var pos = e.GetPosition(DragGhostLayer);
        Canvas.SetLeft(DragGhost, pos.X + 14);
        Canvas.SetTop(DragGhost, pos.Y + 10);
    }

    private void HideDragGhost() => DragGhostLayer.IsVisible = false;

    // ───────── Helpers ─────────

    /// <summary>Resolves the <see cref="CommandSequenceSlot"/> that owns a chip control
    /// by walking up to its <see cref="ItemsControl"/>.</summary>
    private static CommandSequenceSlot ResolveSlot(Control control)
        => control.FindAncestorOfType<ItemsControl>()?.DataContext as CommandSequenceSlot;

    /// <summary>The strip whose drop zone currently contains the pointer, or null.</summary>
    private (Border Zone, ItemsControl List)? StripUnderPointer(PointerEventArgs e)
    {
        foreach (var (zone, list) in _strips)
        {
            var pos = e.GetPosition(zone);
            if (pos.X >= 0 && pos.Y >= 0 &&
                pos.X <= zone.Bounds.Width && pos.Y <= zone.Bounds.Height)
            {
                return (zone, list);
            }
        }

        return null;
    }
}