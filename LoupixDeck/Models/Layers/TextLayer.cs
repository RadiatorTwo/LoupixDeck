using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders text. Inherits position/scale from <see cref="LayerBase"/>;
/// when <see cref="Centered"/> is true, position is interpreted as an offset from
/// the button center, otherwise as the upper-left corner.
/// </summary>
public partial class TextLayer : LayerBase
{
    public const string Kind = "text";

    /// <summary>
    /// Width of the text-layout box in device pixels. The renderer wraps text
    /// at this width and (when <see cref="Centered"/> is true) centers within it.
    /// <c>0</c> means "fall back to the device size" — covers configs persisted
    /// before this property existed.
    /// </summary>
    [ObservableProperty]
    public partial int BoxWidth { get; set; }

    [ObservableProperty]
    public partial int BoxHeight { get; set; }

    [JsonIgnore]
    public int EffectiveBoxWidth => BoxWidth > 0 ? BoxWidth : 90;

    [JsonIgnore]
    public int EffectiveBoxHeight => BoxHeight > 0 ? BoxHeight : 90;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TextSize { get; set; } = 16;

    [ObservableProperty]
    public partial Color TextColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial bool Outlined { get; set; }

    [ObservableProperty]
    public partial Color OutlineColor { get; set; } = Colors.Black;

    [ObservableProperty]
    public partial bool Centered { get; set; } = true;

    public override string LayerKind => Kind;
}
