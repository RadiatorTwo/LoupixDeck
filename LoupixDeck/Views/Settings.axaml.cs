using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class Settings : Window
{
    /// <summary>Gap between two page rows; the drop line sits in its middle.</summary>
    private const double PageRowGap = 8;

    private TouchButtonPage _draggedPage;
    private Control _draggedRow;
    private int _dropIndex = -1;

    public Settings() : this(null) { }

    public Settings(SettingsViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
            {
                dlg.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    // ───────── Touch page reordering (drag handle) ─────────

    private void OnPageGripPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control grip || grip.DataContext is not TouchButtonPage page) return;
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;

        _draggedPage = page;
        _draggedRow = grip.FindAncestorOfType<Border>(includeSelf: false);
        _draggedRow?.Classes.Add("dragging");
        _dropIndex = -1;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnPageGripMoved(object sender, PointerEventArgs e)
    {
        if (_draggedPage == null) return;

        int count = TouchPageList.ItemCount;
        Point position = e.GetPosition(TouchPageList);

        // The insertion index is the first row whose middle lies below the pointer.
        int index = count;
        for (int i = 0; i < count; i++)
        {
            if (TouchPageList.ContainerFromIndex(i) is not Control row) continue;
            if (position.Y < row.Bounds.Top + (row.Bounds.Height / 2))
            {
                index = i;
                break;
            }
        }

        int from = TouchPageList.Items.IndexOf(_draggedPage);
        // Dropping right above or below itself changes nothing, so show no line there.
        if (index == from || index == from + 1)
        {
            _dropIndex = -1;
            TouchPageDropLine.IsVisible = false;
            return;
        }

        _dropIndex = index;
        ShowDropLine(index, count);
    }

    private void ShowDropLine(int index, int count)
    {
        Control anchor = TouchPageList.ContainerFromIndex(Math.Min(index, count - 1));
        if (anchor == null) return;

        // Each container includes its row's bottom margin, the gap, so the line goes half a gap
        // above a row's top, or half a gap above the last container's bottom at the end.
        double y = index < count
            ? anchor.Bounds.Top - (PageRowGap / 2)
            : anchor.Bounds.Bottom - (PageRowGap / 2);

        Canvas.SetLeft(TouchPageDropLine, 0);
        Canvas.SetTop(TouchPageDropLine, y - (TouchPageDropLine.Height / 2));
        TouchPageDropLine.Width = TouchPageList.Bounds.Width;
        TouchPageDropLine.IsVisible = true;
    }

    private void OnPageGripReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_draggedPage == null) return;

        TouchButtonPage page = _draggedPage;
        int dropIndex = _dropIndex;
        e.Pointer.Capture(null);
        EndPageDrag();

        if (dropIndex < 0 || DataContext is not SettingsViewModel vm) return;

        // Removing the page first shifts every later slot up by one.
        int from = TouchPageList.Items.IndexOf(page);
        int target = dropIndex > from ? dropIndex - 1 : dropIndex;
        vm.MoveTouchPage(page, target);
    }

    private void OnPageGripCaptureLost(object sender, PointerCaptureLostEventArgs e) => EndPageDrag();

    private void EndPageDrag()
    {
        _draggedRow?.Classes.Remove("dragging");
        _draggedRow = null;
        _draggedPage = null;
        _dropIndex = -1;
        TouchPageDropLine.IsVisible = false;
    }
}
