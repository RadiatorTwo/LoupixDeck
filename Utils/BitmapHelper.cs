using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LoupixDeck.Models;

namespace LoupixDeck.Utils;

public static class BitmapHelper
{
    public static Bitmap CreateBlackBitmap(int width, int height)
    {
        // Create an empty pixel array (RGBA format)
        var pixelData = new byte[width * height * 4];

        // Fill all pixels with black (RGBA: 0,0,0,255)
        for (int i = 0; i < pixelData.Length; i += 4)
        {
            pixelData[i] = 0;     // Red channel
            pixelData[i + 1] = 0; // Green channel
            pixelData[i + 2] = 0; // Blue channel
            pixelData[i + 3] = 255; // Alpha channel (fully visible)
        }

        // Create a bitmap directly from the pixel data
        using var memoryStream = new MemoryStream();
        using (var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque))
        {
            using var frameBuffer = wb.Lock();
            System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, frameBuffer.Address, pixelData.Length);
            wb.Save(memoryStream);
        }

        memoryStream.Position = 0;
        return new Bitmap(memoryStream);
    }
    
    public static Bitmap RenderSimpleButtonImage(SimpleButton simpleButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(simpleButton);

        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96, 96)
        );

        using var ctx = rtb.CreateDrawingContext(true);

        // Background: first clear it with transparency
        ctx.DrawRectangle(
            brush: Brushes.Transparent,
            pen: null,
            rect: new Rect(0, 0, width, height)
        );
        
        // Example values for ring thickness and margin
        const int ringThickness = 4;
        const int margin = 8;

        // Create a pen for the ring
        var brush = new SolidColorBrush(simpleButton.ButtonColor);
        var ringPen = new Pen(brush, ringThickness);

        // Calculate the center point
        var center = new Point(width / 2.0, height / 2.0);

        // Choose radii to maintain the desired margin from the edges
        var radiusX = (width - 2 * margin) / 2.0;
        var radiusY = (height - 2 * margin) / 2.0;

        // Draw the ring (circle or ellipse depending on width/height ratio)
        ctx.DrawEllipse(
            Brushes.Transparent,
            ringPen,
            center,
            radiusX,
            radiusY
        );

        return rtb;
    }
}
