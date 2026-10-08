using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.ViewModels;
using LoupixDeck.Views.Controls;

namespace LoupixDeck.Utils;

/// <summary>
/// Zooms the device view in the main window (issue #251): Ctrl + mouse wheel anywhere in the window,
/// Ctrl +/- in steps and Ctrl 0 for 100 %. The zoom itself lives on the device's view model, so
/// each device keeps its own; this class decides how the window and the view fit together.
/// </summary>
/// <remarks>
/// Zoom and window size are independent: the window's size never changes the zoom, and zooming
/// never resizes the window. The view sits centred in the window and scrolls when it does not fit.
/// The window only sizes itself to the view when it opens and on a device switch, and then never
/// past its screen's working area.
/// </remarks>
public sealed class DeviceViewZoom
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 3.0;

    // The stops Ctrl +/- move between; the wheel zooms freely in between them.
    private static readonly double[] Steps = [0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    // Zoom factor per wheel notch. Touchpads report fractions of a notch and zoom proportionally.
    private const double WheelFactor = 1.1;

    private readonly Window _window;
    private readonly ScrollViewer _viewport;
    private readonly ContentControl _host;
    private readonly Func<MainWindowViewModel> _device;

    private DeviceZoomPanel _panel;

    public DeviceViewZoom(Window window, ScrollViewer viewport, ContentControl host,
        Func<MainWindowViewModel> device)
    {
        _window = window;
        _viewport = viewport;
        _host = host;
        _device = device;

        ZoomInCommand = new RelayCommand(() => SetZoom(NextStep(CurrentZoom)));
        ZoomOutCommand = new RelayCommand(() => SetZoom(PreviousStep(CurrentZoom)));
        ResetZoomCommand = new RelayCommand(() => SetZoom(1.0));

        AddGesture(Key.OemPlus, ZoomInCommand);
        AddGesture(Key.Add, ZoomInCommand);
        AddGesture(Key.OemMinus, ZoomOutCommand);
        AddGesture(Key.Subtract, ZoomOutCommand);
        AddGesture(Key.D0, ResetZoomCommand);
        AddGesture(Key.NumPad0, ResetZoomCommand);

        // Anywhere in the window, as in a browser. Tunnelling, so a Ctrl + wheel is ours before the
        // viewport (or anything else) scrolls with it.
        _window.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        _window.Opened += (_, _) => Refresh();
        _window.ScalingChanged += (_, _) => UpdateLimit();
        _window.PositionChanged += (_, _) => UpdateLimit();
        _window.Resized += OnWindowResized;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand ResetZoomCommand { get; }

    /// <summary>Raised when the zoom the view shows (or is heading to) changes.</summary>
    public event EventHandler ZoomChanged;

    /// <summary>Raised when the user zoomed.</summary>
    public event EventHandler UserZoomed;

    /// <summary>The zoom the view shows, or is heading to while it animates; 1 is 100 %.</summary>
    public double CurrentZoom => Panel?.TargetZoom ?? _device()?.ViewZoom ?? 1.0;

    private DeviceZoomPanel Panel => (_host.Content as UserControl)?.Content as DeviceZoomPanel;

    // A window whose size is not taken from its content, so a zoom cannot grow it.
    private bool IsFixedWindow =>
        _window.WindowState is WindowState.Maximized or WindowState.FullScreen ||
        (_window.WindowState == WindowState.Normal && _window.SizeToContent != SizeToContent.WidthAndHeight);

    /// <summary>Re-applies the screen limit, e.g. after the device layout has been swapped.</summary>
    public void Refresh()
    {
        DeviceZoomPanel panel = Panel;
        if (!ReferenceEquals(panel, _panel))
        {
            _panel?.TargetZoomChanged -= OnTargetZoomChanged;
            _panel = panel;
            _panel?.TargetZoomChanged += OnTargetZoomChanged;
        }

        UpdateLimit();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTargetZoomChanged(object sender, EventArgs e) => ZoomChanged?.Invoke(this, EventArgs.Empty);

    private void AddGesture(Key key, IRelayCommand command)
    {
        _window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, KeyModifiers.Control), Command = command });
    }

    private void OnWheel(object sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || e.Delta.Y == 0) return;

        SetZoom(CurrentZoom * Math.Pow(WheelFactor, e.Delta.Y));
        e.Handled = true;
    }

    private void SetZoom(double zoom)
    {
        MainWindowViewModel device = _device();
        if (device == null) return;

        if (_window.WindowState == WindowState.Normal && _window.SizeToContent != SizeToContent.Manual)
        {
            // Zooming must not resize the window: pin it at its current size before the view changes.
            Size size = _window.ClientSize;
            _window.SizeToContent = SizeToContent.Manual;
            _window.Width = size.Width;
            _window.Height = size.Height;
        }

        Apply(device, zoom);
    }

    private void Apply(MainWindowViewModel device, double zoom)
    {
        double limit = Panel?.MaxZoom ?? MaxZoom;
        device.ViewZoom = Math.Round(Math.Clamp(zoom, MinZoom, Math.Max(MinZoom, Math.Min(MaxZoom, limit))), 3);
        UserZoomed?.Invoke(this, EventArgs.Empty);
    }

    private static double NextStep(double zoom)
    {
        foreach (double step in Steps)
        {
            if (step > zoom + 0.001) return step;
        }

        return MaxZoom;
    }

    private static double PreviousStep(double zoom)
    {
        for (int i = Steps.Length - 1; i >= 0; i--)
        {
            if (Steps[i] < zoom - 0.001) return Steps[i];
        }

        return MinZoom;
    }

    private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty && e.Property != Window.SizeToContentProperty) return;

        UpdateLimit();
    }

    private static double ZoomFor(double available, double atOne, double atTwo)
    {
        double perZoom = atTwo - atOne;
        return perZoom > 0 ? 1.0 + ((available - atOne) / perZoom) : double.PositiveInfinity;
    }

    /// <summary>Works out how far the view may grow before the window would outgrow its screen.</summary>
    private void UpdateLimit()
    {
        DeviceZoomPanel panel = Panel;
        if (panel == null) return;

        // Only a window that sizes itself to the view can be pushed past the screen by a zoom.
        if (IsFixedWindow)
        {
            panel.MaxZoom = double.PositiveInfinity;
            return;
        }

        Screen screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen == null || _viewport.Bounds.Height <= 0) return;

        Size work = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Size client = _window.ClientSize;
        Size frame = _window.FrameSize ?? client;

        // Everything around the view: window decorations, plus the header and notice rows above it.
        double availableWidth = work.Width - (frame.Width - client.Width);
        double availableHeight = work.Height - (frame.Height - client.Height) - (client.Height - _viewport.Bounds.Height);

        Size atOne = panel.SizeAt(1.0);
        Size atTwo = panel.SizeAt(2.0);
        double limit = Math.Min(
            ZoomFor(availableWidth, atOne.Width, atTwo.Width),
            ZoomFor(availableHeight, atOne.Height, atTwo.Height));

        panel.MaxZoom = Math.Max(MinZoom, Math.Floor(limit * 100) / 100);
    }

    /// <summary>
    /// A window that grew with its view grows to the right and down; if that pushed it past the
    /// working area, it is moved back in.
    /// </summary>
    private void OnWindowResized(object sender, WindowResizedEventArgs e)
    {
        if (e.Reason != WindowResizeReason.Layout || _window.WindowState != WindowState.Normal) return;

        Screen screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen == null) return;

        PixelRect work = screen.WorkingArea;
        PixelSize frame = PixelSize.FromSize(_window.FrameSize ?? _window.ClientSize, _window.DesktopScaling);
        PixelPoint position = _window.Position;

        int x = Math.Max(work.X, Math.Min(position.X, work.Right - frame.Width));
        int y = Math.Max(work.Y, Math.Min(position.Y, work.Bottom - frame.Height));
        if (x != position.X || y != position.Y)
            _window.Position = new PixelPoint(x, y);
    }
}
