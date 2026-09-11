using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Layout;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.ActionPanel;

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
///
/// The same machine carries a row out of the apps and actions panel. That drag always overwrites
/// its target, and a press that never passed the threshold counts as a click assigning the row to
/// the selected button — so the panel needs no pointer handling of its own, and a cancelled drag
/// is torn down by the one <see cref="Reset"/> below.
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

    /// <summary>Edge length of the ghost carrying a panel row: square, like the key it is headed
    /// for, and close to the on-screen size of one.</summary>
    private const double PanelGhostSize = 76;

    /// <summary>Inset of the icon inside that tile, so it reads as an icon on a key rather than as
    /// a full-bleed image — roughly the proportion an assigned icon gets on a real key.</summary>
    private const double PanelGhostInset = 13;

    private readonly Control _root;         // window root Grid: hit-test + capture + coordinate ref
    private readonly Canvas _overlay;       // fills the root Grid; ghost/highlight live here
    private readonly Border _ghost;
    private readonly Image _ghostImage;
    private readonly Border _dropHighlight;
    private readonly Func<MainWindowViewModel> _resolveVm;

    private Button _sourceButton;
    private LoupedeckButton _sourceModel;

    // Set instead of the two above while a panel row is being dragged; the two source kinds are
    // mutually exclusive.
    private PanelItemViewModel _panelItem;

    // True while the gesture is owned by the panel window, which captures the pointer itself and
    // forwards the positions here.
    private bool _external;

    // The visual the ghost is snapshotted from and that fades while dragging: the dragged button,
    // or the panel row.
    private Control _sourceVisual;

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

        MainWindowViewModel vm = _resolveVm();
        if (vm == null) return;

        (Button button, LoupedeckButton model) = FindButton(e.Source as Visual);
        if (button != null && model != null)
        {
            if (!vm.CanDrag(model)) return;

            _sourceButton = button;
            _sourceModel = model;
            _sourceVisual = button;
            _startInRoot = e.GetPosition(_root);
            _dragging = false;
            return;
        }

    }

    // ── Drags out of the panel window ──────────────────────────────────────
    //
    // The panel is its own window, so it owns the pointer for the whole gesture and forwards the
    // positions here already translated into this window's coordinates. Everything past the arming
    // is shared with a drag that started on a button.

    /// <summary>Arms a drag on the panel row under <paramref name="source"/>. Returns false when
    /// the press was not on a row, leaving the panel's own click handling untouched.</summary>
    public bool PanelPointerPressed(Visual source, Point posInRoot)
    {
        if (_dragging || _resolveVm() == null) return false;

        (Control row, PanelItemViewModel item) = FindPanelRow(source);
        if (row == null) return false;

        _panelItem = item;
        _sourceVisual = row;
        _external = true;
        _startInRoot = posInRoot;
        _dragging = false;
        return true;
    }

    /// <summary>
    /// Begins a drag that the panel's own control has already promoted past its threshold — the
    /// command picker does that bookkeeping itself. Returns false when the drag could not start.
    /// </summary>
    public bool PanelDragBegin(PanelItemViewModel item, Control sourceVisual, Point posInRoot)
    {
        if (_dragging || item == null || _resolveVm() == null) return false;

        _panelItem = item;
        _sourceVisual = sourceVisual;
        _external = true;
        _startInRoot = posInRoot;

        if (!TryStartDrag(null))
        {
            Reset();
            return false;
        }

        return true;
    }

    public void PanelPointerMoved(Point posInRoot)
    {
        if (!_external || _sourceVisual == null) return;

        if (!_dragging)
        {
            if (Math.Abs(posInRoot.X - _startInRoot.X) < DragThreshold &&
                Math.Abs(posInRoot.Y - _startInRoot.Y) < DragThreshold)
                return;

            if (!TryStartDrag(null))
            {
                Reset();
                return;
            }
        }

        UpdateGhostPosition(posInRoot);
        UpdateTarget(posInRoot);
        UpdateChrome();

        // The ghost belongs to whichever window the pointer is over: this one draws it once the
        // pointer has crossed in, and the panel draws it until then.
        _ghost.IsVisible = _root.Bounds.Contains(posInRoot);
    }

    /// <summary>
    /// The ghost of a panel drag in flight, so the panel window can draw the same one while the
    /// pointer is still over it. Null whenever no panel drag is running.
    /// </summary>
    public IImage PanelDragGhost => (_external && _dragging) ? _ghostImage.Source : null;

    /// <summary>Size to draw <see cref="PanelDragGhost"/> at, matching this window's ghost.</summary>
    public Size PanelDragGhostSize => new(_ghost.Width, _ghost.Height);

    /// <summary>Inset the icon sits at inside that tile. Handed out rather than repeated in the
    /// panel window, so the ghost cannot end up a different size on the two sides.</summary>
    public Thickness PanelDragGhostPadding => _ghost.Padding;

    public void PanelPointerReleased(Point posInRoot)
    {
        if (!_external) return;

        PanelItemViewModel item = _panelItem;
        bool dragged = _dragging;
        LoupedeckButton target = _targetModel;
        Reset();

        MainWindowViewModel vm = _resolveVm();
        if (vm == null || item == null) return;

        // A press that never became a drag is a click: assign to whatever button is selected. With
        // nothing selected this does nothing, which is what clicking a row before picking a button
        // should do.
        if (!dragged)
        {
            _ = vm.AssignPanelItemToSelectionAsync(item);
            return;
        }

        if (target != null)
            _ = vm.AssignPanelItemAsync(item, target);
    }

    /// <summary>Abandons a panel drag that lost its pointer, leaving nothing on screen.</summary>
    public void PanelDragCancelled()
    {
        if (_external) Reset();
    }

    public void PointerMoved(PointerEventArgs e)
    {
        if (_sourceVisual == null) return;

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

            if (!TryStartDrag(e.Pointer))
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
        MainWindowViewModel vm = _resolveVm();

        if (_dragging)
        {
            bool copy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
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

    /// <param name="pointer">
    /// The pointer to capture, or null for a gesture the panel window already owns — capturing it
    /// here a second time would take it away from the window the events are coming from.
    /// </param>
    private bool TryStartDrag(IPointer pointer)
    {
        Size onScreen;

        if (_panelItem != null)
        {
            // A panel row is a wide list row, so a ghost of the whole row reads nothing like the
            // square key it is headed for. Carry the row's icon on a key-shaped tile instead, inset
            // from the edges the way an assigned icon sits on a real key.
            IImage icon = PanelGhostImage();
            if (icon == null) return false;

            _ghostImage.Source = icon;
            _ghostImage.Stretch = Stretch.Uniform;
            _ghost.Padding = new Thickness(PanelGhostInset);

            onScreen = new Size(PanelGhostSize, PanelGhostSize);
            _ghost.Width = onScreen.Width;
            _ghost.Height = onScreen.Height;

            // Not the shape that was grabbed, so it hangs centred on the cursor.
            _grabOffset = new Point(onScreen.Width / 2, onScreen.Height / 2);
        }
        else
        {
            // Snapshot the button's current content (its inner image, without the selection frame).
            Visual content = (_sourceButton?.Content as Visual) ?? _sourceVisual;
            Size size = content.Bounds.Size;
            if (size.Width < 1 || size.Height < 1) return false;

            int pxW = Math.Max(1, (int)Math.Ceiling(size.Width));
            int pxH = Math.Max(1, (int)Math.Ceiling(size.Height));
            RenderTargetBitmap bitmap = new(new PixelSize(pxW, pxH), new Vector(96, 96));
            bitmap.Render(content);

            _ghostImage.Source = bitmap;
            _ghostImage.Stretch = Stretch.Fill;
            _ghost.Padding = default;

            // Size the ghost to the button's on-screen (Viewbox-scaled) size, and remember where on
            // it the user grabbed so the ghost stays under the cursor at that same spot.
            onScreen = OnScreenSize(_sourceVisual);
            _ghost.Width = onScreen.Width;
            _ghost.Height = onScreen.Height;

            Point? topLeft = _sourceVisual.TranslatePoint(new Point(0, 0), _overlay);
            _grabOffset = topLeft.HasValue
                ? new Point(_startInRoot.X - topLeft.Value.X, _startInRoot.Y - topLeft.Value.Y)
                : new Point(onScreen.Width / 2, onScreen.Height / 2);
        }

        // Fade the source slot so it reads as "picked up" (and, for a move, about to be emptied).
        _sourceVisual.Opacity = SourceDragOpacity;

        _dragging = true;

        if (pointer != null)
        {
            _pointer = pointer;
            _pointer.Capture(_root);

            // Grab keyboard focus so Esc (cancel) and live Ctrl (toggle copy without moving the
            // mouse) are delivered while dragging.
            _root.Focus();

            _topLevel = TopLevel.GetTopLevel(_root);
            _topLevel?.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
            _topLevel?.AddHandler(InputElement.KeyUpEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

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
        DropOperation op;

        if (_panelItem != null)
        {
            // A panel row always overwrites its target, so the only thing the ring has to say is
            // whether this button accepts the row at all.
            op = (_targetButton != null) && (vm?.CanAssignPanelItem(_panelItem, _targetModel) == true)
                ? DropOperation.Copy
                : DropOperation.None;
        }
        else
        {
            op = overOther && vm != null
                ? vm.PreviewDrop(_sourceModel, _targetModel, _copy)
                : DropOperation.None;
        }

        // Drop-target ring: hidden over empty chrome or over the source itself; otherwise coloured
        // by the operation (green move / amber swap / blue copy) or red for an invalid target.
        // A panel drag has no source button to skip, so every button it passes gets a ring.
        bool showRing = _panelItem != null ? _targetButton != null : overOther;
        Point? topLeft = showRing ? _targetButton.TranslatePoint(new Point(0, 0), _overlay) : null;
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

    /// <summary>mdi-gesture-tap-button — ghost glyph for a row that carries no icon at all. Without
    /// a last resort the ghost would have nothing to show and the drag would never start.</summary>
    private const string FallbackGlyph = "󰬣";

    /// <summary>Style classes marking a panel row and its icon, in <c>ActionPanelView.axaml</c>.</summary>
    private const string PanelRowClass = "panel-row";
    private const string PanelRowIconClass = "panel-row-icon";

    /// <summary>
    /// The panel row under <paramref name="source"/>. Matched on the row's own root element rather
    /// than on its data context: every element inside a row inherits that context, and the
    /// innermost of them would give a ghost of a single label instead of the whole row.
    /// </summary>
    private static (Control, PanelItemViewModel) FindPanelRow(Visual source)
    {
        Control row = source?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(c => c.Classes.Contains(PanelRowClass) && c.DataContext is PanelItemViewModel);
        return (row, row?.DataContext as PanelItemViewModel);
    }

    /// <summary>
    /// The image for a panel row's ghost. An application row already holds its extracted icon as a
    /// bitmap, which is used directly — snapshotting the 26px row tile instead would throw away the
    /// resolution the ghost needs. A command row only has a glyph, so its tile is rendered.
    /// </summary>
    private IImage PanelGhostImage()
    {
        if (_panelItem.Icon != null)
            return _panelItem.Icon;

        Visual tile = _sourceVisual?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Classes.Contains(PanelRowIconClass));

        Size size = tile?.Bounds.Size ?? default;
        if (size.Width >= 1 && size.Height >= 1)
        {
            RenderTargetBitmap bitmap = new(
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)),
                new Vector(96, 96));
            bitmap.Render(tile);
            return bitmap;
        }

        return RenderGlyph(string.IsNullOrEmpty(_panelItem.Glyph) ? FallbackGlyph : _panelItem.Glyph);
    }

    /// <summary>
    /// Draws a row's glyph into a ghost-sized tile. Command rows carry their icon as a character
    /// rather than as an element, so there is nothing on screen to snapshot — the glyph is laid out
    /// off screen and rendered instead.
    /// </summary>
    private static IImage RenderGlyph(string glyph)
    {
        if (string.IsNullOrEmpty(glyph))
            return null;

        const int size = (int)PanelGhostSize;
        TextBlock text = new()
        {
            Text = glyph,
            FontFamily = new FontFamily(SymbolLibrary.FontUri),
            FontSize = PanelGhostSize * 0.72,
            Foreground = Brushes.White,
            Width = size,
            Height = size,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        text.Measure(new Size(size, size));
        text.Arrange(new Rect(0, 0, size, size));

        RenderTargetBitmap bitmap = new(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(text);
        return bitmap;
    }

    /// <summary>Assigns a panel row to the selected button, for a panel control that reports a
    /// click rather than a drag.</summary>
    public void PanelAssignToSelection(PanelItemViewModel item)
    {
        if (item == null) return;
        _ = _resolveVm()?.AssignPanelItemToSelectionAsync(item);
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
        if (_sourceVisual != null)
            _sourceVisual.Opacity = 1;

        _sourceButton = null;
        _sourceModel = null;
        _sourceVisual = null;
        _panelItem = null;
        _external = false;
        _targetButton = null;
        _targetModel = null;
        _copy = false;

        _ghostImage.Source = null;
        _ghost.IsVisible = false;
        _dropHighlight.IsVisible = false;
        _overlay.IsVisible = false;
    }
}
