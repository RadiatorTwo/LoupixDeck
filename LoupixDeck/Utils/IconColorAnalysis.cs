using System.Runtime.CompilerServices;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Pixel analysis of imported pack icons: whether an icon is a single-color shape (tintable like a
/// font glyph) and where its visible content sits inside the image.
/// </summary>
public static class IconColorAnalysis
{
    /// <summary>Pixels at or below this alpha are treated as background.</summary>
    private const byte AlphaThreshold = 40;

    /// <summary>Max - min channel difference above which a pixel counts as colored.</summary>
    private const int ChromaThreshold = 48;

    /// <summary>Share of colored pixels above which the icon is colored.</summary>
    private const double ColoredShare = 0.03;

    /// <summary>Spread of the 5th to 95th luminance percentile above which the icon has several tones.</summary>
    private const int LuminanceSpread = 90;

    private sealed class Bounds(SKRectI rect)
    {
        public SKRectI Rect { get; } = rect;
    }

    private static readonly ConditionalWeakTable<SKBitmap, Bounds> ContentBoundsCache = new();

    /// <summary>
    /// True when the visible pixels share one color: gray-ish and of similar brightness. Such an icon
    /// is a shape whose alpha carries all detail, so tinting it loses nothing. A white icon with black
    /// line work has two tones and counts as colored, since a tint would flatten its detail.
    /// </summary>
    public static bool IsMonochrome(SKBitmap bitmap)
    {
        if (!TryGetAlphaLayout(bitmap, out int bytesPerPixel, out bool bgra))
            return false;

        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        int step = Math.Max(1, Math.Max(bitmap.Width, bitmap.Height) / 128);

        int[] histogram = new int[256];
        int visible = 0;
        int colored = 0;

        for (int y = 0; y < bitmap.Height; y += step)
        {
            int row = y * rowBytes;
            for (int x = 0; x < bitmap.Width; x += step)
            {
                int i = row + (x * bytesPerPixel);
                byte a = pixels[i + 3];
                if (a <= AlphaThreshold)
                    continue;

                // Premultiplied pixels are un-premultiplied so edge pixels keep their true color.
                int r = Unpremultiply(pixels[i + (bgra ? 2 : 0)], a, bitmap.AlphaType);
                int g = Unpremultiply(pixels[i + 1], a, bitmap.AlphaType);
                int b = Unpremultiply(pixels[i + (bgra ? 0 : 2)], a, bitmap.AlphaType);

                visible++;
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > ChromaThreshold)
                    colored++;

                histogram[((r * 299) + (g * 587) + (b * 114)) / 1000]++;
            }
        }

        if (visible == 0)
            return true;

        if (colored > visible * ColoredShare)
            return false;

        return Percentile(histogram, visible, 0.95) - Percentile(histogram, visible, 0.05) <= LuminanceSpread;
    }

    /// <summary>
    /// The smallest pixel rectangle holding every visible pixel, cached per bitmap. Pack icons often
    /// carry transparent padding; cropping it lets them fill the layer box like a font glyph does.
    /// Returns the full bitmap when the layout is unknown or nothing is visible.
    /// </summary>
    public static SKRectI GetContentBounds(SKBitmap bitmap)
    {
        return ContentBoundsCache.GetValue(bitmap, static bmp => new Bounds(ComputeContentBounds(bmp))).Rect;
    }

    private static SKRectI ComputeContentBounds(SKBitmap bitmap)
    {
        SKRectI full = new(0, 0, bitmap.Width, bitmap.Height);
        if (!TryGetAlphaLayout(bitmap, out int bytesPerPixel, out _))
            return full;

        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;

        for (int y = 0; y < bitmap.Height; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (pixels[row + (x * bytesPerPixel) + 3] <= 8)
                    continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return right < 0 ? full : new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static bool TryGetAlphaLayout(SKBitmap bitmap, out int bytesPerPixel, out bool bgra)
    {
        bytesPerPixel = 4;
        bgra = bitmap?.ColorType == SKColorType.Bgra8888;

        return bitmap is { Width: > 0, Height: > 0 } &&
               bitmap.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888 &&
               bitmap.GetPixels() != IntPtr.Zero;
    }

    private static int Unpremultiply(byte channel, byte alpha, SKAlphaType alphaType)
        => alphaType == SKAlphaType.Premul && alpha < 255 ? Math.Min(255, channel * 255 / alpha) : channel;

    private static int Percentile(int[] histogram, int total, double fraction)
    {
        int target = (int)(total * fraction);
        int seen = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            seen += histogram[i];
            if (seen > target)
                return i;
        }

        return 255;
    }
}
