using Avalonia;
using Avalonia.Controls;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Lays out the main window's header: a button group on the left, the context selectors and a
/// button group on the right, in that child order. When the selectors fit centred between the two
/// groups, all three share one row; otherwise the selectors drop to a second row of their own.
/// </summary>
/// <remarks>
/// The window sizes itself to the device view, so the header must never be what widens it. The
/// desired width is therefore always the two-row width, and the row decision is taken from the
/// width the panel is actually given. That width is only known on arrange, so a changed decision
/// there triggers one more measure with the new height.
/// </remarks>
public sealed class HeaderBarPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<HeaderBarPanel, double>(nameof(Gap), 12);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<HeaderBarPanel, double>(nameof(RowSpacing), 8);

    /// <summary>Minimum space between the selectors and each button group in one-row mode.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Space between the button row and the selector row in two-row mode.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>Whether the last layout put everything on one row.</summary>
    public bool IsSingleRow { get; private set; }

    static HeaderBarPanel()
    {
        AffectsMeasure<HeaderBarPanel>(GapProperty, RowSpacingProperty);
    }

    private Size LeftSize => SizeOf(0);
    private Size CenterSize => SizeOf(1);
    private Size RightSize => SizeOf(2);

    private Size SizeOf(int index)
        => index < Children.Count && Children[index].IsVisible ? Children[index].DesiredSize : default;

    private bool Fits(double width)
    {
        Size center = CenterSize;
        if (center.Width <= 0) return true;

        double side = Math.Max(LeftSize.Width, RightSize.Width);
        return center.Width + (2 * side) + (2 * Gap) <= width;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(double.PositiveInfinity, double.PositiveInfinity);
        foreach (Control child in Children)
            child.Measure(unbounded);

        // A window sizing to its content offers far more width than it will end up with, so the
        // offer alone would always say one row. Decide from the narrower of the offer and the
        // width last arranged at instead: before the first arrange that is zero, so two rows, and
        // a window that grew is caught by the arrange below.
        IsSingleRow = Fits(Math.Min(availableSize.Width, Bounds.Width));

        return new Size(NarrowWidth(), HeightFor(IsSingleRow));
    }

    private double NarrowWidth()
    {
        Size left = LeftSize;
        Size right = RightSize;
        return Math.Max(left.Width + Gap + right.Width, CenterSize.Width);
    }

    private double HeightFor(bool singleRow)
    {
        double buttons = Math.Max(LeftSize.Height, RightSize.Height);
        double center = CenterSize.Height;

        if (singleRow) return Math.Max(buttons, center);
        return center > 0 ? buttons + RowSpacing + center : buttons;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool singleRow = Fits(finalSize.Width);
        if (singleRow != IsSingleRow)
        {
            // The measured height belongs to the other mode; arrange in the right one now and
            // let the next pass hand the window the matching height.
            IsSingleRow = singleRow;
            InvalidateMeasure();
        }

        Size left = LeftSize;
        Size center = CenterSize;
        Size right = RightSize;

        double rowHeight = singleRow ? finalSize.Height : Math.Max(left.Height, right.Height);

        ArrangeChild(0, new Rect(0, (rowHeight - left.Height) / 2, left.Width, left.Height));
        ArrangeChild(2, new Rect(finalSize.Width - right.Width, (rowHeight - right.Height) / 2, right.Width, right.Height));

        double centerX = (finalSize.Width - center.Width) / 2;
        double centerY = singleRow ? (rowHeight - center.Height) / 2 : rowHeight + RowSpacing;
        ArrangeChild(1, new Rect(centerX, centerY, center.Width, center.Height));

        return finalSize;
    }

    private void ArrangeChild(int index, Rect rect)
    {
        if (index < Children.Count)
            Children[index].Arrange(rect);
    }
}
