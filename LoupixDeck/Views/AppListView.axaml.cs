using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

/// <summary>Payload for the app list's drag lifecycle: the application being dragged plus the
/// live pointer args, so the host can position its ghost and drop marker.</summary>
public sealed class AppDragEventArgs(InstalledApp app, PointerEventArgs pointer) : EventArgs
{
    public InstalledApp App { get; } = app;
    public PointerEventArgs Pointer { get; } = pointer;
}

/// <summary>
/// The installed applications bound to an <see cref="AppPickerViewModel"/>, for a button editor's
/// Apps tab. Raises activation and drag events the way <see cref="CommandPickerView"/> does for
/// commands; the host turns the application into a launch command in its sequence.
/// </summary>
public partial class AppListView : UserControl
{
    private const double DragThreshold = 5;

    // A row armed for a possible drag; promoted to a real drag once the pointer moves past the
    // threshold, so a click or double-click is not swallowed.
    private AppRowViewModel _dragRow;
    private Point _dragStart;
    private bool _dragging;

    /// <summary>Raised when an application should be added (double-click on a row).</summary>
    public event EventHandler<InstalledApp> AppActivated;

    /// <summary>Raised once when a row drag crosses the movement threshold.</summary>
    public event EventHandler<AppDragEventArgs> AppDragStarted;

    /// <summary>Raised on each pointer move while dragging a row.</summary>
    public event EventHandler<AppDragEventArgs> AppDragMoved;

    /// <summary>Raised when a row drag is released (the host decides whether it dropped on target).</summary>
    public event EventHandler<AppDragEventArgs> AppDragReleased;

    public AppListView()
    {
        InitializeComponent();

        // Tunnel and handled-too: the ListBoxItem handles the press itself to select the row.
        AppListBox.AddHandler(PointerPressedEvent, AppList_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AppListBox.AddHandler(PointerMovedEvent, AppList_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AppListBox.AddHandler(PointerReleasedEvent, AppList_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AppListBox.AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (ReferenceEquals(e.Source, AppListBox)) EndDrag();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static AppRowViewModel RowUnder(Visual source)
        => source?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as AppRowViewModel;

    private void AppList_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (RowUnder(e.Source as Visual) is { } row)
            AppActivated?.Invoke(this, row.App);
    }

    private void AppList_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(AppListBox).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;

        _dragRow = RowUnder(e.Source as Visual);
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void AppList_PointerMoved(object sender, PointerEventArgs e)
    {
        if (_dragRow == null) return;

        if (!e.GetCurrentPoint(AppListBox).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        if (!_dragging)
        {
            Point pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) < DragThreshold &&
                Math.Abs(pos.Y - _dragStart.Y) < DragThreshold)
                return;

            // Captured so the moves keep coming once the pointer leaves the list for a strip.
            _dragging = true;
            e.Pointer.Capture(AppListBox);
            AppDragStarted?.Invoke(this, new AppDragEventArgs(_dragRow.App, e));
        }

        AppDragMoved?.Invoke(this, new AppDragEventArgs(_dragRow.App, e));
    }

    private void AppList_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        bool wasDragging = _dragging;
        if (wasDragging && _dragRow != null)
            AppDragReleased?.Invoke(this, new AppDragEventArgs(_dragRow.App, e));

        if (wasDragging)
            e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        _dragRow = null;
        _dragging = false;
    }
}
