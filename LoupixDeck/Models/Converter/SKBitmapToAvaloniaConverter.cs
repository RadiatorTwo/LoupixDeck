using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Models.Converter;

public class SKBitmapToAvaloniaBitmapConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (object)ToBitmap(value as SKBitmap) ?? AvaloniaProperty.UnsetValue;
    }

    /// <summary>Copies a Skia bitmap into an independent Avalonia bitmap; null when it has no pixels.</summary>
    public static Bitmap ToBitmap(SKBitmap skBitmap)
    {
        if (skBitmap == null) return null;

        // The RenderedImage setters retire and dispose old bitmaps under the Skia gate,
        // from whichever thread redraws the key or wheel. Holding the gate for the whole
        // copy keeps the bitmap from being disposed while its native pixels are read.
        lock (SkiaRenderGate.Sync)
        {
            return CopyToBitmap(skBitmap);
        }
    }

    private static Bitmap CopyToBitmap(SKBitmap skBitmap)
    {
        // Checked under the gate: a bitmap disposed before it was read has a zero handle,
        // and calling into native Skia with it would fault.
        if (skBitmap.Handle == IntPtr.Zero || skBitmap.IsNull) return null;

        // Guard: a bitmap that cannot expose its pixels (empty / not peekable)
        // would otherwise NRE / AccessViolation below. PeekPixels returns null
        // in that case.
        using var pixmap = skBitmap.PeekPixels();
        if (pixmap == null) return null;

        var pixels = skBitmap.GetPixels();
        if (pixels == IntPtr.Zero) return null;

        // Derive PixelFormat / AlphaFormat from SKColorType
        var pixelFormat = skBitmap.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => PixelFormat.Bgra8888 // Fallback
        };

        // Label the pixels with the alpha type they were actually written in: Skia
        // bitmaps default to premultiplied, and reading those as unpremultiplied
        // brightens every semi-transparent edge.
        var alphaFormat = skBitmap.AlphaType switch
        {
            SKAlphaType.Opaque => AlphaFormat.Opaque,
            SKAlphaType.Unpremul => AlphaFormat.Unpremul,
            _ => AlphaFormat.Premul
        };

        // Avalonia 11.3's Bitmap(PixelFormat, AlphaFormat, IntPtr, …) constructor
        // copies the pixels, so the returned Bitmap is independent of skBitmap.
        // GC.KeepAlive guarantees skBitmap (and its native pixel buffer) stays
        // alive across the copy.
        var bitmap = new Bitmap(
            pixelFormat,
            alphaFormat,
            pixels,
            new PixelSize(skBitmap.Width, skBitmap.Height),
            new Vector(96, 96),
            skBitmap.RowBytes);

        GC.KeepAlive(skBitmap);
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // throw new NotImplementedException("ConvertBack is not needed.");
        return null;
    }
}