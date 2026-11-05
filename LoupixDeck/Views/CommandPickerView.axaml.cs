using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.Views;

/// <summary>Payload for the picker's command drag lifecycle: the leaf being dragged
/// plus the live pointer args (so the host can position its ghost / drop marker).</summary>
public sealed class CommandDragEventArgs(MenuEntry entry, PointerEventArgs pointer, Control row) : EventArgs
{
    public MenuEntry Entry { get; } = entry;
    public PointerEventArgs Pointer { get; } = pointer;

    /// <summary>The row being dragged, for a host that wants to picture it in a drag ghost.</summary>
    public Control Row { get; } = row;
}

/// <summary>
/// Reusable master-detail command picker (issue #171). Renders the sectioned group
/// rail, search and command list bound to a <see cref="CommandPickerViewModel"/>, and
/// raises activation / drag events carrying the selected leaf <see cref="MenuEntry"/>
/// so the host button-editor can insert it into its command sequence exactly as
/// before (the old tree used the same insertion contract).
/// </summary>
public partial class CommandPickerView : UserControl
{
    // A command row armed for a possible drag-to-insert; promoted to a real drag once
    // the pointer moves past the threshold (so a click / double-click is not swallowed).
    private CommandRowViewModel _dragCandidate;
    private Control _dragRow;
    private Point _dragStart;
    private bool _dragging;

    /// <summary>Raised when a command should be added (double-click or Enter on a row).</summary>
    public event EventHandler<MenuEntry> CommandActivated;

    /// <summary>Raised once when a row drag crosses the movement threshold.</summary>
    public event EventHandler<CommandDragEventArgs> CommandDragStarted;

    /// <summary>Raised on each pointer move while dragging a row.</summary>
    public event EventHandler<CommandDragEventArgs> CommandDragMoved;

    /// <summary>Raised when a row drag is released (the host decides whether it dropped on target).</summary>
    public event EventHandler<CommandDragEventArgs> CommandDragReleased;

    private CommandPickerViewModel _subscribed;

    public CommandPickerView()
    {
        InitializeComponent();
    }

    private CommandPickerViewModel ViewModel => DataContext as CommandPickerViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed != null)
            _subscribed.SelectionRestored -= OnSelectionRestored;
        _subscribed = ViewModel;
        if (_subscribed != null)
            _subscribed.SelectionRestored += OnSelectionRestored;
    }

    // ── Keeping the selection in sight ───────────────────────────────────────

    // After a rebuild the rail and the detail list are new and scrolled to the top. Once they are
    // laid out, scroll the selected category and command back into view.
    private void OnSelectionRestored(object sender, EventArgs e) =>
        Dispatcher.UIThread.Post(BringSelectionIntoView, DispatcherPriority.Background);

    private void BringSelectionIntoView()
    {
        if (ViewModel is not { } vm)
            return;

        BringIntoView(vm.SelectedCategory);
        BringIntoView(vm.SelectedCommand);
    }

    /// <summary>Scrolls the outermost control showing <paramref name="item"/> into view.</summary>
    private void BringIntoView(object item)
    {
        if (item == null)
            return;

        foreach (Visual visual in this.GetVisualDescendants())
        {
            if (visual is Control control && ReferenceEquals(control.DataContext, item) && control.IsEffectivelyVisible)
            {
                control.BringIntoView();
                return;
            }
        }
    }

    // ── Group rail ───────────────────────────────────────────────────────────

    private void RailItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CommandCategoryViewModel category })
            ViewModel?.SelectCategory(category);
    }

    // ── Command rows ─────────────────────────────────────────────────────────

    private void Row_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row } || !row.IsSelectable)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Single click selects (highlights); a drag or double-click adds the command.
        ViewModel?.SelectCommand(row);
        _dragCandidate = row;
        _dragRow = sender as Control;
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void Row_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row } || !row.IsSelectable)
            return;

        CommandActivated?.Invoke(this, row.Entry);
        _dragCandidate = null;
        e.Handled = true;
    }

    /// <summary>"+ Add" button in the footer — the explicit equivalent of a double-click
    /// on the selected row.</summary>
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedCommand is not { } row)
            return;

        CommandActivated?.Invoke(this, row.Entry);
        _dragCandidate = null;
        e.Handled = true;
    }

    private void Row_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row } || !row.IsSelectable)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                CommandActivated?.Invoke(this, row.Entry);
                e.Handled = true;
                break;
            case Key.Space:
                ViewModel?.SelectCommand(row);
                e.Handled = true;
                break;
        }
    }

    // ── Drag lifecycle (pointer captured on this control once promoted) ──────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragCandidate == null)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetDrag(e.Pointer);
            return;
        }

        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4)
                return;

            _dragging = true;
            e.Pointer.Capture(this);
            CommandDragStarted?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e, _dragRow));
        }

        CommandDragMoved?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e, _dragRow));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging && _dragCandidate != null)
            CommandDragReleased?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e, _dragRow));

        ResetDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ResetDrag(null);
    }

    private void ResetDrag(IPointer pointer)
    {
        _dragCandidate = null;
        _dragRow = null;
        _dragging = false;
        pointer?.Capture(null);
    }

    // ── Keyboard shortcuts (search focus / clear) ───────────────────────────

    private void Root_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel is { IsSearching: true } vm)
        {
            vm.SearchText = string.Empty;
            e.Handled = true;
        }
    }
}