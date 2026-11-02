using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One segment of a split Loupedeck CT wheel screen (<see cref="RotaryButton.SplitMode"/>): its
/// own press command, label and optional layer canvas. Turning the wheel stays with the wheel
/// mode; only pressing and the picture are per segment.
/// </summary>
public sealed partial class WheelSegment : ObservableObject
{
    /// <summary>Command sequence run when this segment is tapped. Empty does nothing.</summary>
    [ObservableProperty]
    public partial string Command { get; set; } = string.Empty;

    /// <summary>
    /// Label of the segment, drawn by the automatic layout and by text layers that show the dial
    /// label. Empty by default.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Custom layout of this segment: a layer canvas the size of the whole wheel screen, clipped
    /// to the segment when drawn. Null means the automatic layout (the label at the segment's
    /// centre). Persisted only when set.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomCanvas))]
    [JsonProperty("Canvas", NullValueHandling = NullValueHandling.Ignore)]
    public partial TouchButton Canvas { get; set; }

    /// <summary>True when <see cref="Canvas"/> replaces the automatic layout.</summary>
    [JsonIgnore]
    public bool HasCustomCanvas => Canvas != null;

    /// <summary>
    /// Opaque runtime identity of this segment, handed to plugins as the button key on press and
    /// on the render path of the segment's live layers. Not persisted.
    /// </summary>
    [JsonIgnore]
    public string RuntimeKey { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Post-load wiring of the canvas' layer handlers, like <see cref="RotaryButton.RewireAfterLoad"/>.</summary>
    public void RewireAfterLoad() => Canvas?.RewireLayerHandlers();
}
