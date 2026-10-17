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
    protected override Size MeasureOverride(Size availableSize)
    {
        Size desired = base.MeasureOverride(availableSize);

        // Layoutable clamps this up to MinHeight.
        return new Size(desired.Width, 0);
    }
}
