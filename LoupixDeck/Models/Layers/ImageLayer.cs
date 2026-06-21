using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models.Layers;

/// <summary>
/// A layer that renders an image from the asset folder. The asset is referenced
/// by relative path; the actual bitmap is loaded lazily via the asset service
/// and cached in <see cref="CachedImage"/>.
/// </summary>
public class ImageLayer : LayerBase
{
    public const string Kind = "image";

    /// <summary>Device-pixel frame size the image is fitted into (90×90 button).</summary>
    private const double DeviceBaseSize = 90.0;

    private string _assetRelativePath;
    private string _animatedAssetPath;
    private SerializableRect _sourceRect = SerializableRect.Empty;
    private SKBitmap _cachedImage;

    public string AssetRelativePath
    {
        get => _assetRelativePath;
        set
        {
            if (SetField(ref _assetRelativePath, value))
            {
                _cachedImage = null;
                OnPropertyChanged(nameof(CachedImage));
            }
        }
    }

    /// <summary>
    /// Relative asset path of an animated source (GIF / animated WebP, or a video transcoded once at
    /// import) for this layer. <c>null</c> ⇒ a plain static image — so configs written before issue
    /// #121 deserialize unchanged and keep behaving exactly as before. When set, the central
    /// animation engine drives the layer by swapping the current frame into <see cref="CachedImage"/>,
    /// which the existing image draw path renders without any further change.
    /// </summary>
    public string AnimatedAssetPath
    {
        get => _animatedAssetPath;
        set
        {
            if (SetField(ref _animatedAssetPath, value))
            {
                // The frame source changed: drop the cached frame and let the animation engine
                // (or the editor preview) repopulate it.
                _cachedImage = null;
                OnPropertyChanged(nameof(CachedImage));
                OnPropertyChanged(nameof(IsAnimated));
            }
        }
    }

    /// <summary>True when this layer is driven by an animated source rather than a static image.</summary>
    [JsonIgnore]
    public bool IsAnimated => !string.IsNullOrEmpty(_animatedAssetPath);

    /// <summary>
    /// Swaps the currently displayed animation frame into the backing field WITHOUT raising
    /// <see cref="LayerBase.OnPropertyChanged"/>. The animation engine renders and pushes the button
    /// itself, so firing the change here would double-push and churn the editor binding at frame rate.
    /// Frames are owned by <see cref="IAnimatedImageCache"/> (shared, reused across loops), so the
    /// layer only references them and never disposes them.
    /// </summary>
    public void SetAnimationFrame(SKBitmap frame)
    {
        _cachedImage = frame;
    }

    /// <summary>
    /// Crop window on the original image. <see cref="SerializableRect.Empty"/>
    /// means "use the full image".
    /// </summary>
    public SerializableRect SourceRect
    {
        get => _sourceRect;
        set
        {
            if (SetField(ref _sourceRect, value))
                OnDisplaySizeChanged();
        }
    }

    [JsonIgnore]
    public SKBitmap CachedImage
    {
        get => _cachedImage;
        set
        {
            if (ReferenceEquals(_cachedImage, value)) return;
            _cachedImage = value;
            OnPropertyChanged();
            OnDisplaySizeChanged();
        }
    }

    /// <summary>
    /// Source dimensions used for fitting: the crop window if set, otherwise the
    /// full cached bitmap. Null when neither is available yet.
    /// </summary>
    private (double Width, double Height)? GetSourceDimensions()
    {
        if (!_sourceRect.IsEmpty && _sourceRect.Width > 0 && _sourceRect.Height > 0)
            return (_sourceRect.Width, _sourceRect.Height);

        if (_cachedImage is { Width: > 0, Height: > 0 })
            return (_cachedImage.Width, _cachedImage.Height);

        return null;
    }

    [JsonIgnore]
    public override double DisplayWidth
    {
        get
        {
            if (GetSourceDimensions() is not { } src) return 0;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            return src.Width * fit * EffectiveScaleX;
        }
        set
        {
            if (value <= 0 || GetSourceDimensions() is not { } src) return;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            var baseW = src.Width * fit;
            if (baseW <= 0) return;
            // Lock the current height first so editing width keeps height fixed.
            if (ScaleY <= 0) ScaleY = EffectiveScaleY;
            Scale = value / baseW;
        }
    }

    [JsonIgnore]
    public override double DisplayHeight
    {
        get
        {
            if (GetSourceDimensions() is not { } src) return 0;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            return src.Height * fit * EffectiveScaleY;
        }
        set
        {
            if (value <= 0 || GetSourceDimensions() is not { } src) return;
            var fit = Math.Min(DeviceBaseSize / src.Width, DeviceBaseSize / src.Height);
            var baseH = src.Height * fit;
            if (baseH <= 0) return;
            ScaleY = value / baseH;
        }
    }

    public override string LayerKind => Kind;
}
