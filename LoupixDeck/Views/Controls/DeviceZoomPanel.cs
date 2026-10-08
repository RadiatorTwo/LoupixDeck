using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoupixDeck.Views.Controls;

/// <summary>Which edge of the device body a band child (a page pager) hangs on.</summary>
public enum DeviceBandEdge
{
    Top,
    Bottom
}

/// <summary>
/// Lays out a device view: one body (the SVG chassis plus its control canvas, in SVG units) and
/// the page pagers around it. The body is measured and arranged once at its design size and
/// scaled only through its render transform, so changing the scale never re-measures the deck
/// and costs no more than a re-render. The pagers keep their own size and are placed at body
/// coordinates, so they follow the body at any scale while their text stays readable.
/// </summary>
/// <remarks>
/// A band child is placed horizontally inside the body width minus <see cref="InsetXProperty"/>
/// on either side, honouring its own <see cref="Layoutable.HorizontalAlignment"/>. Vertically,
/// a <see cref="DeviceBandEdge.Top"/> child ends (margin included) at <see cref="AnchorYProperty"/>
/// and a <see cref="DeviceBandEdge.Bottom"/> child starts there, both in SVG units from the
/// body's top. A child wider than its slot is scaled down to fit rather than overlapping its
/// neighbours, which only happens at small scales.
/// </remarks>
public sealed class DeviceZoomPanel : Panel
{
    /// <summary>On-screen width of the body at 100 % zoom, in device-independent pixels.</summary>
    public static readonly StyledProperty<double> BaseWidthProperty =
        AvaloniaProperty.Register<DeviceZoomPanel, double>(nameof(BaseWidth), double.NaN);

    /// <summary>Zoom factor on top of <see cref="BaseWidth"/>; 1 is 100 %. Changes are animated.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DeviceZoomPanel, double>(nameof(Zoom), 1.0);

    /// <summary>
    /// Upper bound applied on top of <see cref="Zoom"/>, so a view that would not fit the screen is
    /// shown smaller without overwriting the zoom the user chose.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<DeviceZoomPanel, double>(nameof(MaxZoom), double.PositiveInfinity);

    public static readonly AttachedProperty<bool> IsBodyProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, bool>("IsBody");

    public static readonly AttachedProperty<DeviceBandEdge> EdgeProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, DeviceBandEdge>("Edge");

    public static readonly AttachedProperty<double> AnchorYProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, double>("AnchorY");

    public static readonly AttachedProperty<double> InsetXProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, double>("InsetX");

    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(160);
    private static readonly CubicEaseOut AnimationEasing = new();

    static DeviceZoomPanel()
    {
        AffectsMeasure<DeviceZoomPanel>(BaseWidthProperty);
        AffectsParentMeasure<DeviceZoomPanel>(IsBodyProperty, EdgeProperty, AnchorYProperty, InsetXProperty);
    }

    // The zoom on screen right now; it trails TargetZoom while an animation runs. NaN until the
    // first measure, which starts out at the target without animating.
    private double _shownZoom = double.NaN;

    // The running animation: the zoom it started from (NaN when none runs) and the frame time of
    // its first frame, set once that frame arrives.
    private double _animationFrom = double.NaN;
    private TimeSpan? _animationStart;
    private bool _frameRequested;

    // The size reported while a zoom animates, so the window is resized once rather than every frame.
    private Size? _heldSize;

    public double BaseWidth
    {
        get => GetValue(BaseWidthProperty);
        set => SetValue(BaseWidthProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>The zoom the view shows once a running animation has finished.</summary>
    public double TargetZoom => Math.Min(Zoom, MaxZoom);

    /// <summary>Raised when <see cref="TargetZoom"/> may have changed.</summary>
    public event EventHandler TargetZoomChanged;

    public static bool GetIsBody(Control control) => control.GetValue(IsBodyProperty);
    public static void SetIsBody(Control control, bool value) => control.SetValue(IsBodyProperty, value);

    public static DeviceBandEdge GetEdge(Control control) => control.GetValue(EdgeProperty);
    public static void SetEdge(Control control, DeviceBandEdge value) => control.SetValue(EdgeProperty, value);

    public static double GetAnchorY(Control control) => control.GetValue(AnchorYProperty);
    public static void SetAnchorY(Control control, double value) => control.SetValue(AnchorYProperty, value);

    public static double GetInsetX(Control control) => control.GetValue(InsetXProperty);
    public static void SetInsetX(Control control, double value) => control.SetValue(InsetXProperty, value);

    /// <summary>Scale from SVG units to device-independent pixels at the zoom shown right now.</summary>
    public double Scale => ScaleFor(double.IsNaN(_shownZoom) ? TargetZoom : _shownZoom);

    /// <summary>The panel's size at a zoom, for working out how far the view can grow.</summary>
    public Size SizeAt(double zoom) => Extent(ScaleFor(zoom)).Size;

    private Control Body
    {
        get
        {
            foreach (Control child in Children)
            {
                if (GetIsBody(child)) return child;
            }

            return null;
        }
    }

    // The body's size in SVG units. Measured against infinity, so after the first pass it comes
    // straight from the layout cache.
    private Size DesignSize => Body?.DesiredSize ?? default;

    private double ScaleFor(double zoom)
    {
        double designWidth = DesignSize.Width;
        double baseScale = designWidth > 0 && !double.IsNaN(BaseWidth) ? BaseWidth / designWidth : 1.0;
        return baseScale * zoom;
    }

    private bool IsZoomAnimating => !double.IsNaN(_animationFrom);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ZoomProperty || change.Property == MaxZoomProperty)
            OnTargetChanged(animate: true);
    }

    private void OnTargetChanged(bool animate)
    {
        TargetZoomChanged?.Invoke(this, EventArgs.Empty);

        TopLevel topLevel = TopLevel.GetTopLevel(this);
        if (!animate || double.IsNaN(_shownZoom) || topLevel == null || !IsEffectivelyVisible)
        {
            // Nothing on screen to animate from, or a change that should follow at once.
            StopAnimation();
            _shownZoom = TargetZoom;
            InvalidateMeasure();
            return;
        }

        if (!IsZoomAnimating && AreClose(_shownZoom, TargetZoom)) return;

        // Start, or restart from wherever the view is now, so a quick series of wheel notches
        // glides on instead of jumping back.
        _animationFrom = _shownZoom;
        _animationStart = null;
        InvalidateMeasure();
        RequestFrame(topLevel);
    }

    private void RequestFrame(TopLevel topLevel)
    {
        if (_frameRequested) return;
        _frameRequested = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _frameRequested = false;
        if (!IsZoomAnimating) return;

        _animationStart ??= time;
        double target = TargetZoom;
        double progress = Math.Clamp((time - _animationStart.Value) / AnimationDuration, 0, 1);
        TopLevel topLevel = TopLevel.GetTopLevel(this);

        if (progress >= 1 || topLevel == null)
        {
            StopAnimation();
            _shownZoom = target;

            // Hand back the room held for the animation.
            InvalidateMeasure();
            return;
        }

        _shownZoom = _animationFrom + ((target - _animationFrom) * AnimationEasing.Ease(progress));

        // A frame only moves things within the size already granted: no measure, no window resize.
        InvalidateArrange();
        RequestFrame(topLevel);
    }

    private void StopAnimation()
    {
        _animationFrom = double.NaN;
        _animationStart = null;
    }

    private static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.0001;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Every child is measured against infinity: the body at its fixed design size, the bands
        // at their natural size. Neither depends on the scale, so a zoom change re-runs only the
        // arithmetic below.
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
        }

        if (double.IsNaN(_shownZoom))
            _shownZoom = TargetZoom;

        Size target = SizeAt(TargetZoom);
        if (!IsZoomAnimating)
        {
            _heldSize = null;
            return target;
        }

        // While the zoom animates, ask for the larger of the start and the end size: growing takes
        // the room up front, shrinking gives it back once the animation is over. Either way the
        // window is resized once, not on every frame, and the view animates inside it.
        Size held = _heldSize ?? SizeAt(_shownZoom);
        _heldSize = new Size(Math.Max(held.Width, target.Width), Math.Max(held.Height, target.Height));
        return _heldSize.Value;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Control body = Body;
        if (body == null) return finalSize;

        double scale = Scale;
        Size design = DesignSize;
        Rect extent = Extent(scale);

        // Centre the content when the panel is given more room than it asked for.
        double originX = (finalSize.Width - extent.Width) / 2;
        double originY = ((finalSize.Height - extent.Height) / 2) - extent.Top;

        body.RenderTransformOrigin = RelativePoint.TopLeft;
        ApplyScale(body, scale);
        body.Arrange(new Rect(originX, originY, design.Width, design.Height));

        foreach (Control child in Children)
        {
            if (ReferenceEquals(child, body)) continue;

            Band band = PlaceBand(child, design, scale);
            Size natural = child.DesiredSize;
            child.RenderTransformOrigin = RelativePoint.TopLeft;

            if (band.Fit < 1)
            {
                // Too wide for its slot: arrange at its natural size and shrink it to fit.
                double width = natural.Width * band.Fit;
                double x = child.HorizontalAlignment switch
                {
                    HorizontalAlignment.Left => band.SlotX,
                    HorizontalAlignment.Right => band.SlotX + band.SlotWidth - width,
                    _ => band.SlotX + ((band.SlotWidth - width) / 2)
                };
                ApplyScale(child, band.Fit);
                child.Arrange(new Rect(originX + x, originY + band.Top, natural.Width, natural.Height));
            }
            else
            {
                ApplyScale(child, 1);
                child.Arrange(new Rect(originX + band.SlotX, originY + band.Top, band.SlotWidth, natural.Height));
            }
        }

        return finalSize;
    }

    /// <summary>The area the body and the bands cover at a scale, relative to the body's top-left.</summary>
    private Rect Extent(double scale)
    {
        Size design = DesignSize;
        double top = 0;
        double bottom = design.Height * scale;

        foreach (Control child in Children)
        {
            if (GetIsBody(child)) continue;

            Band band = PlaceBand(child, design, scale);
            top = Math.Min(top, band.Top);
            bottom = Math.Max(bottom, band.Top + band.Height);
        }

        return new Rect(0, top, design.Width * scale, bottom - top);
    }

    private static Band PlaceBand(Control child, Size design, double scale)
    {
        double inset = GetInsetX(child);
        double slotX = inset * scale;
        double slotWidth = Math.Max(0, (design.Width - (2 * inset)) * scale);
        Size natural = child.DesiredSize;
        double fit = natural.Width > slotWidth && natural.Width > 0 ? slotWidth / natural.Width : 1;
        double height = natural.Height * fit;
        double anchor = GetAnchorY(child) * scale;
        double top = GetEdge(child) == DeviceBandEdge.Top ? anchor - height : anchor;
        return new Band(slotX, slotWidth, top, height, fit);
    }

    // Updates the control's own scale transform in place rather than allocating a new one on every
    // arrange; a changed transform only invalidates rendering, never layout.
    private static void ApplyScale(Control control, double scale)
    {
        if (control.RenderTransform is not ScaleTransform transform)
        {
            if (scale == 1 && control.RenderTransform == null) return;
            transform = new ScaleTransform();
            control.RenderTransform = transform;
        }

        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }

    private readonly record struct Band(double SlotX, double SlotWidth, double Top, double Height, double Fit);
}
