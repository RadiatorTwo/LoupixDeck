using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Models.Macros;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class MacroEditor : Window
{
    // Step currently being dragged via its drag handle; null when no drag is active.
    private MacroStep _draggedStep;

    // ───────── Key capture state ─────────
    // The step a "Capture keys" toggle is currently recording into; null when idle.
    private MacroStep _captureStep;
    private ToggleButton _captureToggle;
    // Keys currently held down (canonical names) and the largest combo seen this capture.
    private readonly List<string> _pressedKeys = [];
    private List<string> _maxCombo = [];

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
            // Stop any active recording so the global hook is uninstalled with the window.
            ViewModel?.StopRecording();

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

    // ───────── Key capture ─────────
    //
    // A "Capture keys" toggle in the KeyCombination / KeyDown / KeyUp editors records
    // real key presses instead of forcing the user to type names. For combination steps
    // the full chord is captured (committed once all keys are released); for single-key
    // steps the first key wins and capture ends immediately.

    private void CaptureToggle_IsCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: MacroStep step } toggle)
            return;

        if (toggle.IsChecked == true)
            BeginCapture(step, toggle);
        else if (ReferenceEquals(toggle, _captureToggle))
            ResetCaptureState();
    }

    private void BeginCapture(MacroStep step, ToggleButton toggle)
    {
        _captureStep = step;
        _captureToggle = toggle;
        _pressedKeys.Clear();
        _maxCombo = [];
        toggle.Focus();
    }

    private void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        if (_captureStep == null)
            return;

        // Swallow every key so it neither toggles the button nor fires app shortcuts.
        e.Handled = true;

        if (!KeyCaptureMap.TryGet(e.Key, out var name))
            return;

        if (_captureStep is KeyCombinationStep combo)
        {
            if (!_pressedKeys.Contains(name))
                _pressedKeys.Add(name);

            // Remember the chord at its widest extent; releasing keys must not shrink it.
            if (_pressedKeys.Count >= _maxCombo.Count)
            {
                _maxCombo = SortModifiersFirst(_pressedKeys);
                combo.Keys = string.Join("+", _maxCombo);
            }
        }
        else
        {
            SetSingleKey(_captureStep, name);
            EndCapture();
        }
    }

    private void Capture_KeyUp(object sender, KeyEventArgs e)
    {
        if (_captureStep == null)
            return;

        e.Handled = true;

        if (KeyCaptureMap.TryGet(e.Key, out var name))
            _pressedKeys.Remove(name);

        // Whole chord released → commit (combo.Keys already holds _maxCombo) and stop.
        if (_captureStep is KeyCombinationStep && _pressedKeys.Count == 0 && _maxCombo.Count > 0)
            EndCapture();
    }

    private void Capture_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_captureStep != null)
            EndCapture();
    }

    private static List<string> SortModifiersFirst(IEnumerable<string> keys)
    {
        // Keep press order but float modifiers to the front (Ctrl+Shift+S, not S+Ctrl).
        return keys.OrderBy(k => IsModifierName(k) ? 0 : 1).ToList();
    }

    private static bool IsModifierName(string name) => name is
        "Ctrl" or "RCtrl" or "Shift" or "RShift" or "Alt" or "AltGr" or "Win" or "Menu";

    private static void SetSingleKey(MacroStep step, string name)
    {
        switch (step)
        {
            case KeyDownStep down: down.Key = name; break;
            case KeyUpStep up: up.Key = name; break;
        }
    }

    private void EndCapture()
    {
        // Unchecking raises CaptureToggle_Unchecked which resets the rest of the state.
        if (_captureToggle != null)
            _captureToggle.IsChecked = false;
        else
            ResetCaptureState();
    }

    private void ResetCaptureState()
    {
        _captureStep = null;
        _captureToggle = null;
        _pressedKeys.Clear();
        _maxCombo = [];
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
