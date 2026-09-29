using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders a tinted glyph from the bundled Material Design Icons
/// font. <see cref="SymbolId"/> references an entry in <see cref="SymbolLibrary"/>;
/// the renderer (<c>BitmapHelper.DrawSymbolLayer</c>) resolves it to a glyph.
/// When <see cref="IconAssetPath"/> is set, the layer instead renders an icon imported
/// from an icon pack, with the same tint, outline, shadow and gradient options.
/// </summary>
public partial class SymbolLayer : LayerBase
{
    public const string Kind = "symbol";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial string SymbolId { get; set; } = string.Empty;

    // --- Icon pack image (issue #300) ---

    /// <summary>
    /// Relative asset path of an icon copied from an icon pack; wins over <see cref="SymbolId"/>.
    /// Null for a glyph, and then omitted from the file, so glyph layers serialize as before.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageIcon))]
    [NotifyPropertyChangedFor(nameof(IsTintable))]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public partial string IconAssetPath { get; set; }

    /// <summary>
    /// Picker key of the pack icon (<c>pack:&lt;id&gt;/&lt;path&gt;</c>), only used to pre-select it
    /// when the icon is picked again. Rendering never depends on the pack still existing.
    /// </summary>
    [ObservableProperty]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public partial string IconSource { get; set; }

    /// <summary>Draws a pack icon in its own colors instead of the tint or gradient.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTintable))]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public partial bool KeepOriginalColors { get; set; }

    /// <summary>True when the layer shows a pack icon rather than a font glyph.</summary>
    [JsonIgnore]
    public bool IsImageIcon => !string.IsNullOrEmpty(IconAssetPath);

    /// <summary>True when the tint and gradient apply; false for a pack icon kept in its own colors.</summary>
    [JsonIgnore]
    public bool IsTintable => !IsImageIcon || !KeepOriginalColors;

    /// <summary>Longest edge of <see cref="IconPreview"/> in pixels.</summary>
    private const int IconPreviewSize = 96;

    private SKBitmap _iconPreview;

    /// <summary>
    /// Small preview of a pack icon for the editor's properties panel, drawn in the tint (or its own
    /// colors) so it looks like the key. Null for a glyph or a missing asset.
    /// </summary>
    [JsonIgnore]
    public SKBitmap IconPreview => _iconPreview ??= BuildIconPreview();

    partial void OnIconAssetPathChanged(string value) => InvalidateIconPreview();

    partial void OnKeepOriginalColorsChanged(bool value) => InvalidateIconPreview();

    partial void OnTintChanged(Color value)
    {
        if (IsImageIcon)
            InvalidateIconPreview();
    }

    private void InvalidateIconPreview()
    {
        SKBitmap old = _iconPreview;
        _iconPreview = null;
        OnPropertyChanged(nameof(IconPreview));
        // The panel's converter copies the pixels, so the old bitmap is no longer used.
        old?.Dispose();
    }

    private SKBitmap BuildIconPreview()
    {
        if (!IsImageIcon || BitmapHelper.AssetResolver?.Invoke(IconAssetPath) is not { } source)
            return null;

        SKRectI src = IconColorAnalysis.GetContentBounds(source);
        if (src.Width <= 0 || src.Height <= 0)
            return null;

        float fit = (float)IconPreviewSize / Math.Max(src.Width, src.Height);
        SKBitmap preview = new(new SKImageInfo(
            Math.Max(1, (int)Math.Round(src.Width * fit)),
            Math.Max(1, (int)Math.Round(src.Height * fit)),
            SKColorType.Bgra8888, SKAlphaType.Premul));

        using SKCanvas canvas = new(preview);
        using SKColorFilter tint = IsTintable
            ? SKColorFilter.CreateBlendMode(new SKColor(Tint.R, Tint.G, Tint.B, Tint.A), SKBlendMode.SrcIn)
            : null;
        using SKPaint paint = new() { ColorFilter = tint };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, src, new SKRect(0, 0, preview.Width, preview.Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        return preview;
    }

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
        get => DeviceBaseSize * EffectiveScaleX;
        set
        {
            if (value <= 0) return;
            // Lock the current height first so editing width keeps height fixed.
            if (ScaleY <= 0) ScaleY = EffectiveScaleY;
            Scale = value / DeviceBaseSize;
        }
    }

    [JsonIgnore]
    public override double DisplayHeight
    {
        get => DeviceBaseSize * EffectiveScaleY;
        set
        {
            if (value <= 0) return;
            ScaleY = value / DeviceBaseSize;
        }
    }

    /// <summary>
    /// Sizes the layer box to the glyph's own aspect ratio, fitted into a square of
    /// <paramref name="size"/> (a <see cref="LayerBase.Scale"/> multiplier). The renderer stretches
    /// the glyph into the box, so a square box would distort every non-square icon.
    /// </summary>
    public void FitScaleToGlyph(double size)
    {
        double ratio = SymbolLibrary.TryGet(SymbolId, out SymbolDefinition def) ? SymbolLibrary.GlyphAspectRatio(def) : 1.0;

        Scale = size * Math.Min(1.0, ratio);
        ScaleY = size * Math.Min(1.0, 1.0 / ratio);
    }

    /// <summary>
    /// Sizes the layer box to <paramref name="aspectRatio"/> (width / height), fitted into a square
    /// of <paramref name="size"/>. Used for pack icons, whose ratio comes from their pixels.
    /// </summary>
    public void FitScaleToAspect(double size, double aspectRatio)
    {
        double ratio = aspectRatio > 0 && double.IsFinite(aspectRatio) ? aspectRatio : 1.0;

        Scale = size * Math.Min(1.0, ratio);
        ScaleY = size * Math.Min(1.0, 1.0 / ratio);
    }

    public override string LayerKind => Kind;
}
