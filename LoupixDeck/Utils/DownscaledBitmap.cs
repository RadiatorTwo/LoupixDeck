using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// A bitmap decoded smaller than the image it stands for. An image layer keeps its geometry, its
/// crop window above all, in the pixels of the source image, and that is persisted in the config;
/// so wherever such geometry meets this bitmap it is measured against <see cref="SourceWidth"/> ×
/// <see cref="SourceHeight"/> and only scaled onto the actual pixels when drawing (see
/// <see cref="BitmapHelper.SourceSizeOf"/> and <see cref="BitmapHelper.ToBitmapPixels"/>).
/// </summary>
public sealed class DownscaledBitmap(SKImageInfo info, int sourceWidth, int sourceHeight) : SKBitmap(info)
{
    /// <summary>Width of the source image, in its own pixels.</summary>
    public int SourceWidth { get; } = sourceWidth;

    /// <summary>Height of the source image, in its own pixels.</summary>
    public int SourceHeight { get; } = sourceHeight;
}
