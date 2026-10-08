using LoupixDeck.PluginSdk;

namespace LoupixDeck.Utils;

/// <summary>
/// What a dial-driven surface knows at render time: the dial's label and the value its
/// adjustment command last reported (null when it is not an adjustment command). A touch key
/// whose command reports a value (<c>IValueDisplayCommand</c>) renders with one too, without a
/// label. Handed down
/// the layer renderer so <see cref="Models.Layers.DialIndicatorLayer"/> and text layers with a
/// dial <see cref="Models.Layers.TextSource"/> can draw live content without the layers
/// themselves holding mutable runtime state.
/// </summary>
public readonly record struct DialRenderContext(string Label, AdjustmentValue? Value)
{
    /// <summary>The value text, or null when there is no value or it has no text.</summary>
    public string ValueText => Value?.Text;

    /// <summary>The value's secondary text, or null when there is none.</summary>
    public string ValueDetail => Value?.Detail;

    /// <summary>The normalised position, or null when there is no value or no position (<c>NaN</c>).</summary>
    public double? Normalized =>
        Value is { Normalized: var n } && !double.IsNaN(n) ? n : null;
}
