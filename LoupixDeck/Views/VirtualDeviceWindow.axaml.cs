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

    public VirtualDeviceWindow()
    {
        InitializeComponent();

        _panel = this.FindControl<Image>("PanelScreen");
        _wheel = this.FindControl<Image>("WheelScreen");

        WireScreen(_panel, (vm, id, x, y, end) => vm.TouchPanel(id, x, y, end));
        WireScreen(_wheel, (vm, id, x, y, end) => vm.TouchWheel(id, x, y, end));
        _wheel.PointerWheelChanged += (_, e) =>
        {
            ViewModel?.RotateWheel(Notches(e));
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
            if (ViewModel is not { } vm) return;
            e.Pointer.Capture(screen);
            Point p = e.GetPosition(screen);
            touch(vm, e.Pointer.Id, p.X, p.Y, false);
            e.Handled = true;
        };
        screen.PointerMoved += (_, e) =>
        {
            if (ViewModel is not { } vm || !ReferenceEquals(e.Pointer.Captured, screen)) return;
            Point p = e.GetPosition(screen);
            touch(vm, e.Pointer.Id, p.X, p.Y, false);
        };
        screen.PointerReleased += (_, e) =>
        {
            if (ViewModel is not { } vm || !ReferenceEquals(e.Pointer.Captured, screen)) return;
            Point p = e.GetPosition(screen);
            touch(vm, e.Pointer.Id, p.X, p.Y, true);
            e.Pointer.Capture(null);
            e.Handled = true;
        };
    }

    private void OnControlPressed(object sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || ControlAt(e.Source) is not { } border) return;
        if (border.DataContext is not VirtualControlViewModel control) return;

        _pressed = border;
        border.Classes.Add("pressed");
        e.Pointer.Capture(border);
        vm.Press(control, down: true);
        e.Handled = true;
    }

    private void OnControlReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_pressed is not { } border) return;
        _pressed = null;
        border.Classes.Remove("pressed");
        e.Pointer.Capture(null);

        if (ViewModel is { } vm && border.DataContext is VirtualControlViewModel control)
            vm.Press(control, down: false);
    }

    private void OnControlWheel(object sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { } vm || ControlAt(e.Source) is not { } border) return;
        if (!border.Classes.Contains("dial") || border.DataContext is not VirtualControlViewModel control) return;

        vm.Rotate(control, Notches(e));
        e.Handled = true;
    }

    /// <summary>The dial or button the event started on, if any.</summary>
    private static Border ControlAt(object source) =>
        (source as Visual)?.GetSelfAndVisualAncestors()
        .OfType<Border>()
        .FirstOrDefault(b => b.Classes.Contains("dial") || b.Classes.Contains("round") || b.Classes.Contains("named"));

    /// <summary>One detent per wheel notch; scrolling up turns clockwise.</summary>
    private static int Notches(PointerWheelEventArgs e) =>
        e.Delta.Y switch
        {
            > 0 => Math.Max(1, (int)Math.Round(e.Delta.Y)),
            < 0 => Math.Min(-1, (int)Math.Round(e.Delta.Y)),
            _ => 0
        };
}
