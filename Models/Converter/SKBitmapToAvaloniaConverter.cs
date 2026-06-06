using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace LoupixDeck.Models.Converter;

public class SKBitmapToAvaloniaBitmapConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SKBitmap { IsNull: false } skBitmap) return AvaloniaProperty.UnsetValue;

        // Guard: a bitmap that cannot expose its pixels (empty / not peekable)
        // would otherwise NRE / AccessViolation below. PeekPixels returns null
        // in that case.
        using var pixmap = skBitmap.PeekPixels();
        if (pixmap == null) return AvaloniaProperty.UnsetValue;

        var pixels = skBitmap.GetPixels();
        if (pixels == IntPtr.Zero) return AvaloniaProperty.UnsetValue;

        // Derive PixelFormat / AlphaFormat from SKColorType
        var pixelFormat = skBitmap.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => PixelFormat.Bgra8888 // Fallback
        };

        var alphaFormat = skBitmap.AlphaType == SKAlphaType.Opaque
            ? AlphaFormat.Opaque
            : AlphaFormat.Unpremul;

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