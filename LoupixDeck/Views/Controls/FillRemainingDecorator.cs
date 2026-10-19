using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Hosts a self-scrolling child (a list) in a star row of a panel that itself sits in a vertical
/// <see cref="ScrollViewer"/>. The scroll viewer measures its content with unbounded height, so a
/// plain star row would grow to the list's full content and the list would lose its own scrolling.
/// This decorator reports only its <see cref="Layoutable.MinHeight"/> as desired height: the outer
/// panel fills the viewport and the row takes the remaining space, and the outer scroll viewer only
/// starts scrolling once that remaining space drops below the minimum.
/// </summary>
public sealed class FillRemainingDecorator : Decorator
{
    // The height the last arrange gave us; NaN until the first one.
    private double _arrangedHeight = double.NaN;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measured with more height than it is given (unbounded in an outer scroll viewer, the screen's
        // height in a window sizing itself to its content), the child would size itself to all of its
        // content and its own scroll viewer would see nothing to scroll, the overflow merely clipped.
        // Measure it with the height it is arranged in instead.
        if (_arrangedHeight < availableSize.Height)
            availableSize = availableSize.WithHeight(_arrangedHeight);

        Size desired = base.MeasureOverride(availableSize);

        // Layoutable clamps this up to MinHeight.
        return new Size(desired.Width, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Measure the child again right here with the height it gets: left to the next pass, the
        // controls inside it whose own size did not change would keep their stale arrangement.
        if (finalSize.Height != _arrangedHeight)
        {
            _arrangedHeight = finalSize.Height;
            Child?.Measure(finalSize.Deflate(Padding));
        }

        return base.ArrangeOverride(finalSize);
    }
}
