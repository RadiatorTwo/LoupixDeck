using Avalonia;
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

    /// <summary>Zoom factor on top of <see cref="BaseWidth"/>; 1 is 100 %.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DeviceZoomPanel, double>(nameof(Zoom), 1.0);

    public static readonly AttachedProperty<bool> IsBodyProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, bool>("IsBody");

    public static readonly AttachedProperty<DeviceBandEdge> EdgeProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, DeviceBandEdge>("Edge");

    public static readonly AttachedProperty<double> AnchorYProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, double>("AnchorY");

    public static readonly AttachedProperty<double> InsetXProperty =
        AvaloniaProperty.RegisterAttached<DeviceZoomPanel, Control, double>("InsetX");

    static DeviceZoomPanel()
    {
        AffectsMeasure<DeviceZoomPanel>(BaseWidthProperty, ZoomProperty);
        AffectsParentMeasure<DeviceZoomPanel>(IsBodyProperty, EdgeProperty, AnchorYProperty, InsetXProperty);
    }

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

    public static bool GetIsBody(Control control) => control.GetValue(IsBodyProperty);
    public static void SetIsBody(Control control, bool value) => control.SetValue(IsBodyProperty, value);

    public static DeviceBandEdge GetEdge(Control control) => control.GetValue(EdgeProperty);
    public static void SetEdge(Control control, DeviceBandEdge value) => control.SetValue(EdgeProperty, value);

    public static double GetAnchorY(Control control) => control.GetValue(AnchorYProperty);
    public static void SetAnchorY(Control control, double value) => control.SetValue(AnchorYProperty, value);

    public static double GetInsetX(Control control) => control.GetValue(InsetXProperty);
    public static void SetInsetX(Control control, double value) => control.SetValue(InsetXProperty, value);

    /// <summary>Scale from SVG units to device-independent pixels at the current zoom.</summary>
    public double Scale => ScaleFor(Zoom);

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

    protected override Size MeasureOverride(Size availableSize)
    {
        // Every child is measured against infinity: the body at its fixed design size, the bands
        // at their natural size. Neither depends on the scale, so a zoom change re-runs only the
        // arithmetic below.
        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
        }

        return Extent(Scale).Size;
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
