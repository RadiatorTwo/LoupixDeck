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
        
        var pixmap = skBitmap.PeekPixels(); // no Copy!
        var info = pixmap.Info;

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
            
        unsafe
        {
            fixed (void* ptr = &pixmap.GetPixelSpan().GetPinnableReference())
            {
                using var stream = new UnmanagedMemoryStream((byte*)ptr, info.RowBytes * info.Height);
                return new Bitmap(
                    pixelFormat,
                    alphaFormat,
                    skBitmap.GetPixels(),
                    new PixelSize(skBitmap.Width, skBitmap.Height),
                    new Vector(96, 96),
                    skBitmap.RowBytes);
            }
        }

    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // throw new NotImplementedException("ConvertBack is not needed.");
        return null;
    }
}