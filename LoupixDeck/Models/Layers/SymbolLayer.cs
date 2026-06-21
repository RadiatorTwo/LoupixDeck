using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders a tinted glyph from the bundled Material Design Icons
/// font. <see cref="SymbolId"/> references an entry in <see cref="SymbolLibrary"/>;
/// the renderer (<c>BitmapHelper.DrawSymbolLayer</c>) resolves it to a glyph.
/// </summary>
public partial class SymbolLayer : LayerBase
{
    public const string Kind = "symbol";

    /// <summary>
    /// Glyph base size in device-pixel space — matches <c>Math.Min(width,height)</c>
    /// for a 90×90 button in <c>BitmapHelper.DrawSymbolLayer</c>.
    /// </summary>
    private const double BaseSize = 90.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial string SymbolId { get; set; } = string.Empty;

    /// <summary>Solid fill color — used when <see cref="UseGradient"/> is false.</summary>
    [ObservableProperty]
    public partial Color Tint { get; set; } = Colors.White;

    // --- Outline ---

    [ObservableProperty]
    public partial bool Outlined { get; set; }

    [ObservableProperty]
    public partial Color OutlineColor { get; set; } = Colors.Black;

    /// <summary>Outline stroke width in device pixels.</summary>
    [ObservableProperty]
    public partial double OutlineWidth { get; set; } = 3.0;

    // --- Drop shadow ---

    [ObservableProperty]
    public partial bool Shadow { get; set; }

    [ObservableProperty]
    public partial Color ShadowColor { get; set; } = Color.FromArgb(160, 0, 0, 0);

    /// <summary>Gaussian blur sigma for the shadow, in device pixels. 0 = sharp.</summary>
    [ObservableProperty]
    public partial double ShadowBlur { get; set; } = 3.0;

    [ObservableProperty]
    public partial int ShadowOffsetX { get; set; } = 2;

    [ObservableProperty]
    public partial int ShadowOffsetY { get; set; } = 2;

    // --- Gradient fill ---

    /// <summary>When true the glyph is filled with a linear gradient instead of <see cref="Tint"/>.</summary>
    [ObservableProperty]
    public partial bool UseGradient { get; set; }

    [ObservableProperty]
    public partial Color GradientStartColor { get; set; } = Colors.White;

    [ObservableProperty]
    public partial Color GradientEndColor { get; set; } = Color.FromRgb(0x60, 0x60, 0x60);

    /// <summary>Gradient direction in degrees: 0° = left→right, 90° = top→bottom.</summary>
    [ObservableProperty]
    public partial double GradientAngle { get; set; } = 90.0;

    /// <summary>
    /// The UTF-16 glyph string for the current <see cref="SymbolId"/>, or empty
    /// if unknown. Used by the editor's properties panel to preview the symbol.
    /// </summary>
    [JsonIgnore]
    public string Glyph => SymbolLibrary.TryGet(SymbolId, out var def) ? def.Glyph : string.Empty;

    [JsonIgnore]
    public override double DisplayWidth
    {
        get => BaseSize * EffectiveScaleX;
        set
        {
            if (value <= 0) return;
            // Lock the current height first so editing width keeps height fixed.
            if (ScaleY <= 0) ScaleY = EffectiveScaleY;
            Scale = value / BaseSize;
        }
    }

    [JsonIgnore]
    public override double DisplayHeight
    {
        get => BaseSize * EffectiveScaleY;
        set
        {
            if (value <= 0) return;
            ScaleY = value / BaseSize;
        }
    }

    public override string LayerKind => Kind;
}
