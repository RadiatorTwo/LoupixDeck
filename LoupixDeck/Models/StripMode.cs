namespace LoupixDeck.Models;

/// <summary>
/// Rendering mode of a side display strip (Razer Stream Controller). Selectable per
/// side strip (left/right). See issue #114.
/// </summary>
public enum StripMode
{
    /// <summary>
    /// Default: the strip is split into one region per adjacent dial, each showing
    /// that knob's label; swipe pages the side's rotary pages. <see cref="RotarySide"/>.
    /// </summary>
    Segmented = 0,

    /// <summary>
    /// The whole 60×270 strip is one freely-drawn canvas (image/text/symbol layers,
    /// edited like a touch button). The dials keep their per-page commands but the
    /// strip content is decoupled from them — no per-knob labels.
    /// </summary>
    FreeDraw = 1,

    /// <summary>
    /// A plugin takes over rendering the strip. Reserved for phase 2b; treated as
    /// <see cref="Segmented"/> until implemented.
    /// </summary>
    PluginOverride = 2
}
