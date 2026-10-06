namespace LoupixDeck.Models.Layers;

/// <summary>
/// Where a <see cref="TextLayer"/> takes its text from. <see cref="Static"/> is the layer's own
/// <see cref="TextLayer.Text"/>; the dial sources draw what the surface's dial reports at render
/// time and ignore <see cref="TextLayer.Text"/>. Persisted as an int; the default is omitted so
/// layers written before this existed are unchanged.
/// </summary>
public enum TextSource
{
    /// <summary>The layer's own text.</summary>
    Static = 0,

    /// <summary>The dial's label (the wheel mode's display text).</summary>
    DialLabel = 1,

    /// <summary>The value text the dial's adjustment command reports, e.g. "-32.5 dB".</summary>
    DialValue = 2
}
