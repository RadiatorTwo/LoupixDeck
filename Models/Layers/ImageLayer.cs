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

    private string _assetRelativePath;
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
    /// Crop window on the original image. <see cref="SerializableRect.Empty"/>
    /// means "use the full image".
    /// </summary>
    public SerializableRect SourceRect
    {
        get => _sourceRect;
        set => SetField(ref _sourceRect, value);
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
        }
    }

    public override string LayerKind => Kind;
}
