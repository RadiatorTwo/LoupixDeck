using Avalonia.Media;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders a tinted glyph from the bundled Material Design Icons
/// font. <see cref="SymbolId"/> references an entry in <see cref="SymbolLibrary"/>;
/// the renderer (<c>BitmapHelper.DrawSymbolLayer</c>) resolves it to a glyph.
/// </summary>
public class SymbolLayer : LayerBase
{
    public const string Kind = "symbol";

    /// <summary>
    /// Glyph base size in device-pixel space — matches <c>Math.Min(width,height)</c>
    /// for a 90×90 button in <c>BitmapHelper.DrawSymbolLayer</c>.
    /// </summary>
    private const double BaseSize = 90.0;

    private string _symbolId = string.Empty;
    private Color _tint = Colors.White;

    private bool _outlined;
    private Color _outlineColor = Colors.Black;
    private double _outlineWidth = 3.0;

    private bool _shadow;
    private Color _shadowColor = Color.FromArgb(160, 0, 0, 0);
    private double _shadowBlur = 3.0;
    private int _shadowOffsetX = 2;
    private int _shadowOffsetY = 2;

    private bool _useGradient;
    private Color _gradientStartColor = Colors.White;
    private Color _gradientEndColor = Color.FromRgb(0x60, 0x60, 0x60);
    private double _gradientAngle = 90.0;

    public string SymbolId
    {
        get => _symbolId;
        set
        {
            if (SetField(ref _symbolId, value))
                OnPropertyChanged(nameof(Glyph));
        }
    }

    /// <summary>Solid fill color — used when <see cref="UseGradient"/> is false.</summary>
    public Color Tint
    {
        get => _tint;
        set => SetField(ref _tint, value);
    }

    // --- Outline ---

    public bool Outlined
    {
        get => _outlined;
        set => SetField(ref _outlined, value);
    }

    public Color OutlineColor
    {
        get => _outlineColor;
        set => SetField(ref _outlineColor, value);
    }

    /// <summary>Outline stroke width in device pixels.</summary>
    public double OutlineWidth
    {
        get => _outlineWidth;
        set => SetField(ref _outlineWidth, value);
    }

    // --- Drop shadow ---

    public bool Shadow
    {
        get => _shadow;
        set => SetField(ref _shadow, value);
    }

    public Color ShadowColor
    {
        get => _shadowColor;
        set => SetField(ref _shadowColor, value);
    }

    /// <summary>Gaussian blur sigma for the shadow, in device pixels. 0 = sharp.</summary>
    public double ShadowBlur
    {
        get => _shadowBlur;
        set => SetField(ref _shadowBlur, value);
    }

    public int ShadowOffsetX
    {
        get => _shadowOffsetX;
        set => SetField(ref _shadowOffsetX, value);
    }

    public int ShadowOffsetY
    {
        get => _shadowOffsetY;
        set => SetField(ref _shadowOffsetY, value);
    }

    // --- Gradient fill ---

    /// <summary>When true the glyph is filled with a linear gradient instead of <see cref="Tint"/>.</summary>
    public bool UseGradient
    {
        get => _useGradient;
        set => SetField(ref _useGradient, value);
    }

    public Color GradientStartColor
    {
        get => _gradientStartColor;
        set => SetField(ref _gradientStartColor, value);
    }

    public Color GradientEndColor
    {
        get => _gradientEndColor;
        set => SetField(ref _gradientEndColor, value);
    }

    /// <summary>Gradient direction in degrees: 0° = left→right, 90° = top→bottom.</summary>
    public double GradientAngle
    {
        get => _gradientAngle;
        set => SetField(ref _gradientAngle, value);
    }

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
