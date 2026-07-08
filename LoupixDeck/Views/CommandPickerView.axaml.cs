using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.Views;

/// <summary>Payload for the picker's command drag lifecycle: the leaf being dragged
/// plus the live pointer args (so the host can position its ghost / drop marker).</summary>
public sealed class CommandDragEventArgs(MenuEntry entry, PointerEventArgs pointer) : EventArgs
{
    public MenuEntry Entry { get; } = entry;
    public PointerEventArgs Pointer { get; } = pointer;
}

/// <summary>
/// Reusable card-based command picker (issue #171). Renders the sectioned category
/// grid, search and detail list bound to a <see cref="CommandPickerViewModel"/>, and
/// raises activation / drag events carrying the selected leaf <see cref="MenuEntry"/>
/// so the host button-editor can insert it into its command sequence exactly as
/// before (the old tree used the same insertion contract).
/// </summary>
public partial class CommandPickerView : UserControl
{
    // A command row armed for a possible drag-to-insert; promoted to a real drag once
    // the pointer moves past the threshold (so a click / double-click is not swallowed).
    private CommandRowViewModel _dragCandidate;
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

    public CommandPickerView()
    {
        InitializeComponent();
    }

    private CommandPickerViewModel ViewModel => DataContext as CommandPickerViewModel;

    // ── Category cards ───────────────────────────────────────────────────────

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CommandCategoryViewModel category })
            ViewModel?.SelectCategory(category);
    }

    // ── Command rows ─────────────────────────────────────────────────────────

    private void Row_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row })
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Single click selects (highlights); a drag or double-click adds the command.
        ViewModel?.SelectCommand(row);
        _dragCandidate = row;
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void Row_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row })
            return;

        CommandActivated?.Invoke(this, row.Entry);
        _dragCandidate = null;
        e.Handled = true;
    }

    private void Row_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: CommandRowViewModel row })
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
            CommandDragStarted?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e));
        }

        CommandDragMoved?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging && _dragCandidate != null)
            CommandDragReleased?.Invoke(this, new CommandDragEventArgs(_dragCandidate.Entry, e));

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