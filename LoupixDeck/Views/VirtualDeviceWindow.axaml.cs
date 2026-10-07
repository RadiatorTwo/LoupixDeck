using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

/// <summary>
/// The simulator of a virtual device (see <see cref="Utils.VirtualDevice"/>). Code-behind only
/// translates pointer gestures into hardware input on the view model: a press, drag and release
/// on a screen is a touch, the mouse wheel over a dial or the wheel turns it, and a press on a
/// button is held until it is released.
/// </summary>
public partial class VirtualDeviceWindow : Window
{
    // One window per virtual device; re-opening it brings the existing one forward.
    private static readonly Dictionary<string, VirtualDeviceWindow> OpenWindows = new(StringComparer.OrdinalIgnoreCase);

    private readonly Image _panel;
    private readonly Image _wheel;

    // The control a press started on, so its release reaches the same button even when the
    // pointer has left it by then.
    private Border _pressed;

    // The touch in progress per screen and where it last was, so a lost capture can still lift it.
    private readonly Dictionary<Image, (int PointerId, Point Position)> _activeTouches = new();

    // Unspent scroll per target: a trackpad reports fractions of a notch, which add up to detents.
    private readonly Dictionary<object, double> _scrollRemainders = new();

    public VirtualDeviceWindow()
    {
        InitializeComponent();

        _panel = this.FindControl<Image>("PanelScreen");
        _wheel = this.FindControl<Image>("WheelScreen");

        WireScreen(_panel, (vm, id, x, y, end) => vm.TouchPanel(id, x, y, end));
        WireScreen(_wheel, (vm, id, x, y, end) => vm.TouchWheel(id, x, y, end));
        _wheel.PointerWheelChanged += (_, e) =>
        {
            ViewModel?.RotateWheel(Notches(_wheel, e));
            e.Handled = true;
        };

        AddHandler(PointerPressedEvent, OnControlPressed, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnControlReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnControlWheel, RoutingStrategies.Bubble);
    }

    private VirtualDeviceViewModel ViewModel => DataContext as VirtualDeviceViewModel;

    /// <summary>Opens the simulator of the virtual device <paramref name="scopeKey"/>, or brings it forward.</summary>
    public static void ShowFor(string scopeKey, LoupedeckDevice.Device.LoupedeckDevice device, string modelName)
    {
        if (device?.VirtualState == null)
            return;

        if (OpenWindows.TryGetValue(scopeKey, out VirtualDeviceWindow existing))
        {
            existing.Activate();
            return;
        }

        VirtualDeviceViewModel viewModel = new(device, modelName);
        VirtualDeviceWindow window = new() { DataContext = viewModel };
        OpenWindows[scopeKey] = window;
        window.Closed += (_, _) =>
        {
            OpenWindows.Remove(scopeKey);
            viewModel.Dispose();
        };
        window.Show();
    }

    private delegate void TouchHandler(VirtualDeviceViewModel viewModel, int pointerId, double x, double y, bool end);

    /// <summary>Press, drag and release on a screen become touch start, move and end frames.
    /// The pointer is captured so a drag that leaves the screen still ends there.</summary>
    private void WireScreen(Image screen, TouchHandler touch)
    {
        screen.PointerPressed += (_, e) =>
        {
            if (ViewModel is not { } vm || !IsPrimaryPress(e, screen) || _activeTouches.ContainsKey(screen)) return;
            Point p = e.GetPosition(screen);
            _activeTouches[screen] = (e.Pointer.Id, p);
            e.Pointer.Capture(screen);
            touch(vm, e.Pointer.Id, p.X, p.Y, false);
            e.Handled = true;
        };
        screen.PointerMoved += (_, e) =>
        {
            if (ViewModel is not { } vm || !_activeTouches.TryGetValue(screen, out var active) ||
                active.PointerId != e.Pointer.Id) return;
            Point p = e.GetPosition(screen);
            _activeTouches[screen] = (e.Pointer.Id, p);
            touch(vm, e.Pointer.Id, p.X, p.Y, false);
        };
        screen.PointerReleased += (_, e) =>
        {
            if (!_activeTouches.TryGetValue(screen, out var active) || active.PointerId != e.Pointer.Id) return;
            // Removed before the capture is released, so the capture-lost handler finds no touch.
            _activeTouches.Remove(screen);
            Point p = e.GetPosition(screen);
            if (ViewModel is { } vm)
                touch(vm, e.Pointer.Id, p.X, p.Y, true);
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        // Focus moved away (Alt-Tab, a dialog) in the middle of a touch: lift the finger where it
        // last was, otherwise the device would keep the touch held for good.
        screen.PointerCaptureLost += (_, _) =>
        {
            if (!_activeTouches.Remove(screen, out var active) || ViewModel is not { } vm) return;
            touch(vm, active.PointerId, active.Position.X, active.Position.Y, true);
        };
    }

    private void OnControlPressed(object sender, PointerPressedEventArgs e)
    {
        if (_pressed != null || ViewModel is not { } vm || ControlAt(e.Source) is not { } border) return;
        if (!IsPrimaryPress(e, border) || border.DataContext is not VirtualControlViewModel control) return;

        _pressed = border;
        border.Classes.Add("pressed");
        border.PointerCaptureLost += OnPressedCaptureLost;
        e.Pointer.Capture(border);
        vm.Press(control, down: true);
        e.Handled = true;
    }

    private void OnControlReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_pressed == null) return;
        ReleasePressed();
        e.Pointer.Capture(null);
    }

    /// <summary>Focus moved away while a button was held: release it rather than leave it held.</summary>
    private void OnPressedCaptureLost(object sender, PointerCaptureLostEventArgs e) => ReleasePressed();

    private void ReleasePressed()
    {
        if (_pressed is not { } border) return;
        _pressed = null;
        border.PointerCaptureLost -= OnPressedCaptureLost;
        border.Classes.Remove("pressed");

        if (ViewModel is { } vm && border.DataContext is VirtualControlViewModel control)
            vm.Press(control, down: false);
    }

    /// <summary>Only the primary button touches or presses; a right or middle click does nothing.</summary>
    private static bool IsPrimaryPress(PointerPressedEventArgs e, Visual relativeTo) =>
        e.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed;

    private void OnControlWheel(object sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { } vm || ControlAt(e.Source) is not { } border) return;
        if (!border.Classes.Contains("dial") || border.DataContext is not VirtualControlViewModel control) return;

        vm.Rotate(control, Notches(control, e));
        e.Handled = true;
    }

    /// <summary>The dial or button the event started on, if any.</summary>
    private static Border ControlAt(object source) =>
        (source as Visual)?.GetSelfAndVisualAncestors()
        .OfType<Border>()
        .FirstOrDefault(b => b.Classes.Contains("dial") || b.Classes.Contains("round") || b.Classes.Contains("named"));

    /// <summary>
    /// Whole detents for a scroll on <paramref name="target"/>; scrolling up turns clockwise. A
    /// mouse wheel reports one unit per notch, a trackpad a stream of fractions — those are
    /// accumulated, otherwise every tiny trackpad event would turn a full detent.
    /// </summary>
    private int Notches(object target, PointerWheelEventArgs e)
    {
        double total = _scrollRemainders.GetValueOrDefault(target) + e.Delta.Y;
        int notches = (int)Math.Truncate(total);
        _scrollRemainders[target] = total - notches;
        return notches;
    }
}
