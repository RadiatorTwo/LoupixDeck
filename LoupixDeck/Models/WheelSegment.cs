using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One segment of a split Loupedeck CT wheel screen (<see cref="RotaryButton.SplitMode"/>): its
/// own press command and layer canvas. What the segment shows is defined only by that canvas,
/// edited in the wheel's appearance editor. Turning the wheel stays with the wheel mode; only
/// pressing and the picture are per segment.
/// </summary>
public sealed partial class WheelSegment : ObservableObject
{
    /// <summary>Command sequence run when this segment is tapped. Empty does nothing.</summary>
    [ObservableProperty]
    public partial string Command { get; set; } = string.Empty;

    /// <summary>
    /// What this segment shows: a layer canvas the size of the whole wheel screen, clipped to the
    /// segment when drawn. The appearance editor creates it; until then (null) the segment is
    /// black. Persisted only when set.
    /// </summary>
    [ObservableProperty]
    [JsonProperty("Canvas", NullValueHandling = NullValueHandling.Ignore)]
    public partial TouchButton Canvas { get; set; }

    /// <summary>
    /// Opaque runtime identity of this segment, handed to plugins as the button key on press and
    /// on the render path of the segment's live layers. Not persisted.
    /// </summary>
    [JsonIgnore]
    public string RuntimeKey { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Post-load wiring of the canvas' layer handlers, like <see cref="RotaryButton.RewireAfterLoad"/>.</summary>
    public void RewireAfterLoad() => Canvas?.RewireLayerHandlers();
}
