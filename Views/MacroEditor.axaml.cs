using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Models.Macros;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class MacroEditor : Window
{
    // Step currently being dragged via its drag handle; null when no drag is active.
    private MacroStep _draggedStep;

    public MacroEditor() : this(null)
    {
    }

    public MacroEditor(MacroEditorViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        Closing += (_, _) =>
        {
            // Changes apply instantly (debounced) — persist anything still pending.
            ViewModel?.FlushPendingChanges();

            if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
            {
                dlg.DialogResult.TrySetResult(new DialogResult(true));
            }
        };
    }

    private MacroEditorViewModel ViewModel => DataContext as MacroEditorViewModel;

    // ───────── Add / Edit / Remove steps ─────────

    private void AddStepMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string stepType })
            ViewModel?.AddStepCommand.Execute(stepType);
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroStep step })
            ViewModel?.RemoveStep(step);
    }

    // ───────── Command tree (CommandStep editor) ─────────

    private void CommandTree_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2)
            return;

        if (e.Source is not TextBlock { DataContext: MenuEntry menuEntry } textBlock ||
            string.IsNullOrWhiteSpace(menuEntry.Command))
            return;

        // The TreeView's Tag carries the CommandStep this tree belongs to.
        var treeView = textBlock.FindAncestorOfType<TreeView>();
        if (treeView?.Tag is CommandStep step)
            ViewModel?.InsertCommandIntoStep(step, menuEntry);
    }

    // ───────── Drag & drop live reorder ─────────
    //
    // The drag handle captures the pointer onto the steps ItemsControl (a stable
    // control that survives collection moves), then every PointerMoved maps the
    // pointer position to a target index and moves the dragged step there
    // immediately — the list reorders live while dragging.

    private void DragHandle_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MacroStep step })
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _draggedStep = step;
        step.IsDragging = true;

        // Capture on the ItemsControl: containers may be recycled during moves,
        // but the list itself stays alive for the whole drag.
        e.Pointer.Capture(StepsList);
        e.Handled = true;
    }

    private void StepsList_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_draggedStep == null)
            return;

        var steps = ViewModel?.SelectedMacro?.Steps;
        if (steps == null)
            return;

        var currentIndex = steps.IndexOf(_draggedStep);
        if (currentIndex < 0)
            return;

        var targetIndex = FindTargetIndex(e, currentIndex, steps.Count);
        if (targetIndex != currentIndex)
            steps.Move(currentIndex, targetIndex);
    }

    private void StepsList_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        EndDrag(e.Pointer);
    }

    private void StepsList_PointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
    {
        EndDrag(null);
    }

    private void EndDrag(IPointer pointer)
    {
        if (_draggedStep == null)
            return;

        _draggedStep.IsDragging = false;
        _draggedStep = null;
        pointer?.Capture(null);
    }

    /// <summary>
    /// Maps the pointer position to the index the dragged step should occupy.
    /// Uses container midpoints as switch thresholds so the order only changes
    /// once the pointer has clearly entered a neighbouring panel (no flicker
    /// with differently sized panels, e.g. expanded inline editors).
    /// </summary>
    private int FindTargetIndex(PointerEventArgs e, int currentIndex, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i == currentIndex)
                continue;

            var container = StepsList.ContainerFromIndex(i);
            if (container == null)
                continue;

            var position = e.GetPosition(container);
            if (position.Y < 0 || position.Y > container.Bounds.Height)
                continue;

            var midpoint = container.Bounds.Height / 2;

            // Dragging upwards: switch once the pointer is in the upper half of the
            // hovered panel; downwards: once it is in the lower half.
            if (i < currentIndex && position.Y < midpoint)
                return i;
            if (i > currentIndex && position.Y > midpoint)
                return i;
        }

        return currentIndex;
    }
}
