using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Utils;

/// <summary>
/// Drag &amp; drop of the device buttons and side displays (issue #166 phase 3), driven from a
/// single window-level overlay. Wired via tunneling pointer handlers on the window so it sees the
/// gesture regardless of the buttons, and a drag only actually starts once the pointer moves past
/// a threshold — so a plain click still selects and a double-click still edits.
///
/// Semantics (matched by <see cref="MainWindowViewModel.DropAsync"/>): without Ctrl an empty
/// target is a move and a non-empty target is a swap; with Ctrl it is a copy that overwrites the
/// target. The ghost is a slightly translucent snapshot of the dragged button in its current
/// state with a rounded frame, grabbed at the pointer offset; the source slot fades while
/// dragging; the drop-target ring's colour previews the pending operation (green move / amber
/// swap / blue copy / red invalid).
/// </summary>
public sealed class DeviceDragDrop
{
    private const double DragThreshold = 5;

    // Drop-target ring / operation-label colours: move = green, swap = amber, copy = blue,
    // invalid = red.
    private static readonly IBrush MoveBrush = new SolidColorBrush(Color.Parse("#FF3DDB6E"));
    private static readonly IBrush SwapBrush = new SolidColorBrush(Color.Parse("#FFE8A33D"));
    private static readonly IBrush CopyBrush = new SolidColorBrush(Color.Parse("#FF3D9BFF"));
    private static readonly IBrush InvalidBrush = new SolidColorBrush(Color.Parse("#FFE5484D"));

    private const double SourceDragOpacity = 0.35;

    private readonly Control _root;         // window root Grid: hit-test + capture + coordinate ref
    private readonly Canvas _overlay;       // fills the root Grid; ghost/highlight live here
    private readonly Border _ghost;
    private readonly Image _ghostImage;
    private readonly Border _dropHighlight;
    private readonly Func<MainWindowViewModel> _resolveVm;

    private Button _sourceButton;
    private LoupedeckButton _sourceModel;
    private Point _startInRoot;
    private Point _grabOffset;              // cursor offset from the button's top-left at grab time
    private bool _dragging;
    private bool _copy;
    private IPointer _pointer;
    private TopLevel _topLevel;

    private Button _targetButton;
    private LoupedeckButton _targetModel;

    public DeviceDragDrop(Control root, Canvas overlay, Border ghost, Image ghostImage,
        Border dropHighlight, Func<MainWindowViewModel> resolveVm)
    {
        _root = root;
        _overlay = overlay;
        _ghost = ghost;
        _ghostImage = ghostImage;
        _dropHighlight = dropHighlight;
        _resolveVm = resolveVm;
    }

    // Arm a possible drag. Does not capture or handle, so a click still selects / edits.
    public void PointerPressed(PointerPressedEventArgs e)
    {
        if (_dragging) return;
        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed) return;

        (Button button, LoupedeckButton model) = FindButton(e.Source as Visual);
        if (button == null || model == null) return;

        MainWindowViewModel vm = _resolveVm();
        if (vm == null || !vm.CanDrag(model)) return;

        _sourceButton = button;
        _sourceModel = model;
        _startInRoot = e.GetPosition(_root);
        _dragging = false;
    }

    public void PointerMoved(PointerEventArgs e)
    {
        if (_sourceButton == null) return;

        // The gesture is only valid while the left button is held. If it isn't — e.g. the release
        // was swallowed because a double-click opened a modal editor — disarm instead of starting
        // or continuing a phantom drag.
        if (!e.GetCurrentPoint(_root).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }

        Point pos = e.GetPosition(_root);

        if (!_dragging)
        {
            if (Math.Abs(pos.X - _startInRoot.X) < DragThreshold &&
                Math.Abs(pos.Y - _startInRoot.Y) < DragThreshold)
                return;

            if (!TryStartDrag(e))
            {
                Reset();
                return;
            }
        }

        _copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        UpdateGhostPosition(pos);
        UpdateTarget(pos);
        UpdateChrome();
        e.Handled = true;
    }

    public void PointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            bool copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            MainWindowViewModel vm = _resolveVm();
            if (vm != null && _targetModel != null && vm.CanDropOnto(_sourceModel, _targetModel))
                _ = vm.DropAsync(_sourceModel, _targetModel, copy);
            e.Handled = true;
        }

        Reset();
    }

    public void PointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Only abort when OUR drag capture (on the root) is lost. Ignore the button's own
        // capture-loss that fires when we take capture over at drag start, and our own release
        // in Reset() (which clears _dragging first).
        if (_dragging && ReferenceEquals(e.Source, _root)) Reset();
    }

    private bool TryStartDrag(PointerEventArgs e)
    {
        Size size = _sourceButton.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return false;

        // Snapshot the button's current content (its inner image, without the selection frame).
        Visual content = _sourceButton.Content as Visual ?? _sourceButton;
        int pxW = Math.Max(1, (int)Math.Ceiling(size.Width));
        int pxH = Math.Max(1, (int)Math.Ceiling(size.Height));
        RenderTargetBitmap bitmap = new(new PixelSize(pxW, pxH), new Vector(96, 96));
        bitmap.Render(content);
        _ghostImage.Source = bitmap;

        // Size the ghost to the button's on-screen (Viewbox-scaled) size, and remember where on
        // the button the user grabbed so the ghost stays under the cursor at that same spot.
        Size onScreen = OnScreenSize(_sourceButton);
        _ghost.Width = onScreen.Width;
        _ghost.Height = onScreen.Height;

        Point? btnTopLeft = _sourceButton.TranslatePoint(new Point(0, 0), _overlay);
        _grabOffset = btnTopLeft.HasValue
            ? new Point(_startInRoot.X - btnTopLeft.Value.X, _startInRoot.Y - btnTopLeft.Value.Y)
            : new Point(onScreen.Width / 2, onScreen.Height / 2);

        // Fade the source slot so it reads as "picked up" (and, for a move, about to be emptied).
        _sourceButton.Opacity = SourceDragOpacity;

        _dragging = true;
        _pointer = e.Pointer;
        _pointer.Capture(_root);

        // Grab keyboard focus so Esc (cancel) and live Ctrl (toggle copy without moving the
        // mouse) are delivered while dragging.
        _root.Focus();

        _topLevel = TopLevel.GetTopLevel(_root);
        _topLevel?.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        _topLevel?.AddHandler(InputElement.KeyUpEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);

        _overlay.IsVisible = true;
        _ghost.IsVisible = true;
        return true;
    }

    private void UpdateGhostPosition(Point pos)
    {
        Canvas.SetLeft(_ghost, pos.X - _grabOffset.X);
        Canvas.SetTop(_ghost, pos.Y - _grabOffset.Y);
    }

    private void UpdateTarget(Point pos)
    {
        (Button button, LoupedeckButton model) = FindButton(_root.InputHitTest(pos) as Visual);
        _targetButton = button;
        _targetModel = model;
    }

    private void UpdateChrome()
    {
        MainWindowViewModel vm = _resolveVm();
        bool overOther = _targetButton != null && !ReferenceEquals(_targetModel, _sourceModel);
        DropOperation op = overOther && vm != null
            ? vm.PreviewDrop(_sourceModel, _targetModel, _copy)
            : DropOperation.None;

        // Drop-target ring: hidden over empty chrome or over the source itself; otherwise coloured
        // by the operation (green move / amber swap / blue copy) or red for an invalid target.
        Point? topLeft = overOther ? _targetButton.TranslatePoint(new Point(0, 0), _overlay) : null;
        if (topLeft == null)
        {
            _dropHighlight.IsVisible = false;
        }
        else
        {
            Size size = OnScreenSize(_targetButton);
            Canvas.SetLeft(_dropHighlight, topLeft.Value.X);
            Canvas.SetTop(_dropHighlight, topLeft.Value.Y);
            _dropHighlight.Width = size.Width;
            _dropHighlight.Height = size.Height;
            _dropHighlight.BorderBrush = BrushFor(op);
            _dropHighlight.IsVisible = true;
        }
    }

    private static IBrush BrushFor(DropOperation op) => op switch
    {
        DropOperation.Move => MoveBrush,
        DropOperation.Swap => SwapBrush,
        DropOperation.Copy => CopyBrush,
        _ => InvalidBrush
    };

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_dragging) return;

        if (e.Key == Key.Escape)
        {
            Reset();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _copy = e.RoutedEvent == InputElement.KeyDownEvent;
            UpdateChrome();
        }
    }

    private Size OnScreenSize(Visual visual)
    {
        Point? topLeft = visual.TranslatePoint(new Point(0, 0), _overlay);
        Point? bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), _overlay);
        if (topLeft == null || bottomRight == null) return visual.Bounds.Size;
        return new Size(Math.Abs(bottomRight.Value.X - topLeft.Value.X),
                        Math.Abs(bottomRight.Value.Y - topLeft.Value.Y));
    }

    private static (Button, LoupedeckButton) FindButton(Visual source)
    {
        Button button = source?
            .GetSelfAndVisualAncestors()
            .OfType<Button>()
            .FirstOrDefault(b => b.CommandParameter is LoupedeckButton);
        return (button, button?.CommandParameter as LoupedeckButton);
    }

    private void Reset()
    {
        _dragging = false;

        IPointer pointer = _pointer;
        _pointer = null;
        pointer?.Capture(null);

        if (_topLevel != null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKey);
            _topLevel.RemoveHandler(InputElement.KeyUpEvent, OnKey);
            _topLevel = null;
        }

        // Restore the faded source slot before dropping the reference.
        if (_sourceButton != null)
            _sourceButton.Opacity = 1;

        _sourceButton = null;
        _sourceModel = null;
        _targetButton = null;
        _targetModel = null;
        _copy = false;

        _ghostImage.Source = null;
        _ghost.IsVisible = false;
        _dropHighlight.IsVisible = false;
        _overlay.IsVisible = false;
    }
}
