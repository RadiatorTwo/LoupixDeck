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

    /// <summary>
    /// Layout box for a surface that is <paramref name="deviceWidth"/> pixels wide. The
    /// renderer must use this rather than <see cref="EffectiveBoxWidth"/>: it is handed the
    /// real surface size, whereas <see cref="LayerBase.DeviceBaseWidth"/> is a projection the
    /// editor stamps and the render path never touches — so on a 96px key the fallback would
    /// otherwise stay at the default 90 and wrap text three pixels early on every edge.
    /// </summary>
    public int ResolveBoxWidth(int deviceWidth) => BoxWidth > 0 ? BoxWidth : deviceWidth;

    /// <inheritdoc cref="ResolveBoxWidth"/>
    public int ResolveBoxHeight(int deviceHeight) => BoxHeight > 0 ? BoxHeight : deviceHeight;

    /// <summary>Editor-facing box size, resolved against the surface the editor is showing.</summary>
    [JsonIgnore]
    public int EffectiveBoxWidth => ResolveBoxWidth(DeviceBaseWidth);

    /// <inheritdoc cref="EffectiveBoxWidth"/>
    [JsonIgnore]
    public int EffectiveBoxHeight => ResolveBoxHeight(DeviceBaseHeight);

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
