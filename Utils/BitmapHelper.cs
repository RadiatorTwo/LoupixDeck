using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LoupixDeck.Models;
using SkiaSharp;
using Avalonia.Platform;

namespace LoupixDeck.Utils;

public static class BitmapHelper
{
    public enum ScalingOption
    {
        None, // Image shown as is in full resolution
        Fill, // The image fills the screen, the aspect ratio may be lost
        Fit, // The image is scaled to be completely visible, the aspect ratio is retained
        Stretch, // The image is distorted to fill the screen completely
        Tile, // The image is displayed several times next to each other/repeatedly
        Center, // The image is displayed centered without scaling
        //CropToFill // Like “Fill”, but with cropping instead of distortion
    }

    public static Bitmap RenderSimpleButtonImage(SimpleButton simpleButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(simpleButton);

        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height)
        );

        using var ctx = rtb.CreateDrawingContext(true);

        // Background: first clear it with transparency
        ctx.DrawRectangle(
            brush: Brushes.Transparent,
            pen: null,
            rect: new Rect(0, 0, width, height)
        );

        // Values for ring thickness and margin
        const int ringThickness = 3;
        const int margin = 8;
        const int innerRingThickness = 4;
        const int innerRingMargin = 28;
        const double gapAngle = 45.0;
        const double startAngle = 60;

        // Create a pen for the ring
        var brush = new ImmutableSolidColorBrush(simpleButton.ButtonColor);
        var ringPen = new ImmutablePen(brush, ringThickness);

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

        // Radii for the inner ring
        var innerRadiusX = (width - 2 * innerRingMargin) / 2.0;
        var innerRadiusY = (height - 2 * innerRingMargin) / 2.0;

        var innerRingPen = new ImmutablePen(brush, innerRingThickness);

        // We have no DrawArc, so we need to draw it with geometry ourselves
        var geo = new StreamGeometry();
        using (var geoCtx = geo.Open())
        {
            const double endAngle = startAngle + (360 - gapAngle);
            const int segmentCount = 100;
            const double angleStep = (endAngle - startAngle) / segmentCount;

            var isFirstPoint = true;

            for (var i = 0; i <= segmentCount; i++)
            {
                var angle = startAngle + i * angleStep;
                var radian = Math.PI * angle / 180.0;
                var x = center.X + innerRadiusX * Math.Cos(radian);
                var y = center.Y - innerRadiusY * Math.Sin(radian);

                var point = new Point(x, y);
                if (isFirstPoint)
                {
                    geoCtx.BeginFigure(point, false);
                    isFirstPoint = false;
                }
                else
                {
                    geoCtx.LineTo(point);
                }
            }
        }

        // Draw the inner ring with the geometry
        ctx.DrawGeometry(Brushes.Transparent, innerRingPen, geo);

        return rtb;
    }

    /// <summary>
    /// Renders the content of a TouchButton (background, image, text) into an Avalonia bitmap.
    /// </summary>
    public static RenderTargetBitmap RenderTouchButtonContent(
        TouchButton touchButton,
        LoupedeckConfig config,
        int width,
        int height,
        int gridColumns = 0)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        // Create SKBitmap for rendering
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        if (config.Wallpaper != null && gridColumns > 0)
        {
            // Determine the position of the button in the grid
            var col = touchButton.Index % gridColumns;
            var row = touchButton.Index / gridColumns;

            // Calculate the section from the wallpaper
            var wallpaperBitmap = config.Wallpaper.ToSKBitmap();
            var srcRect = new SKRect(
                col * width,
                row * height,
                (col + 1) * width,
                (row + 1) * height
            );
            var destRect = new SKRect(0, 0, width, height);

            // Draw Wallpaper Cutout
            canvas.DrawBitmap(wallpaperBitmap, srcRect, destRect);

            // Semi-transparent background
            using var paint = new SKPaint();

            paint.Color = new SKColor(0, 0, 0, (byte)(255 * config.WallpaperOpacity));

            canvas.DrawRect(destRect, paint);
        }
        else
        {
            // Draw Monochrome Background
            canvas.Clear(touchButton.BackColor.ToSKColor());
        }

        if (touchButton.Image != null)
        {
            var imageBitmap = touchButton.Image.ToSKBitmap();
            var destRect = new SKRect(0, 0, width, height);
            canvas.DrawBitmap(imageBitmap, destRect);
        }

        if (!string.IsNullOrEmpty(touchButton.Text))
        {
            DrawTextAt(
                canvas,
                touchButton.Text,
                touchButton.TextColor.ToSKColor(),
                touchButton.TextSize,
                touchButton.TextCentered,
                touchButton.TextPositionX,
                touchButton.TextPositionY,
                width,
                height,
                touchButton.Bold,
                touchButton.Italic,
                touchButton.Outlined,
                touchButton.OutlineColor.ToSKColor()
            );
        }

        // Convert back to RenderTargetBitmap and save in the TouchButton
        var rtb = bitmap.ToRenderTargetBitmap();
        touchButton.RenderedImage = rtb;

        return rtb;
    }

    /// <summary>
    /// Scales and positions a bitmap and returns the result as a new SKBitmap.
    /// </summary>
    public static SKBitmap ScaleAndPositionBitmap(
        SKBitmap source,
        int targetWidth,
        int targetHeight,
        float imageScale,
        int posX,
        int posY,
        ScalingOption scalingOption)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new SKBitmap(targetWidth, targetHeight, source.ColorType, source.AlphaType);

        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true,
            IsDither = true
        };

        SKRect destRect;

        var scaleValue = imageScale / 100;

        switch (scalingOption)
        {
            case ScalingOption.None:
            {
                var scaledWidth = source.Width * scaleValue;
                var scaledHeight = source.Height * scaleValue;
                destRect = new SKRect(posX, posY, posX + scaledWidth, posY + scaledHeight);
                break;
            }

            case ScalingOption.Stretch:
            {
                destRect = new SKRect(posX, posY, posX + targetWidth, posY + targetHeight);
                break;
            }


            case ScalingOption.Fill:
            {
                var scale = Math.Max(
                    (double)targetWidth / source.Width, 
                    (double)targetHeight / source.Height) * scaleValue;
                var scaledWidth = (float)(source.Width * scale);
                var scaledHeight = (float)(source.Height * scale);
                var offsetX = posX + (targetWidth - scaledWidth) / 2;
                var offsetY = posY + (targetHeight - scaledHeight) / 2;
                destRect = new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);
                break;
            }

            case ScalingOption.Fit:
            {
                var scale = Math.Min(
                    (double)targetWidth / source.Width, 
                    (double)targetHeight / source.Height) * scaleValue;
                var scaledWidth = (float)(source.Width * scale);
                var scaledHeight = (float)(source.Height * scale);
                var offsetX = posX + (targetWidth - scaledWidth) / 2;
                var offsetY = posY + (targetHeight - scaledHeight) / 2;
                destRect = new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);
                break;
            }

            case ScalingOption.Tile:
            {
                var scaledWidth = source.Width * scaleValue;
                var scaledHeight = source.Height * scaleValue;

                var startX = posX % (int)scaledWidth;
                var startY = posY % (int)scaledHeight;

                if (startX > 0) startX -= (int)scaledWidth;
                if (startY > 0) startY -= (int)scaledHeight;

                for (var x = startX; x < targetWidth; x += (int)scaledWidth)
                {
                    for (var y = startY; y < targetHeight; y += (int)scaledHeight)
                    {
                        var tileRect = new SKRect(x, y, x + scaledWidth, y + scaledHeight);
                        canvas.DrawBitmap(source, tileRect, paint);
                    }
                }

                return result;
            }

            case ScalingOption.Center:
            {
                var scaledWidth = source.Width * scaleValue;
                var scaledHeight = source.Height * scaleValue;
                var offsetX = posX + (targetWidth - scaledWidth) / 2;
                var offsetY = posY + (targetHeight - scaledHeight) / 2;
                destRect = new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(scalingOption), scalingOption, null);
        }

        canvas.DrawBitmap(source, destRect, paint);
        canvas.Flush();

        return result;
    }

    /// <summary>
    /// Creates an Avalonia RenderTargetBitmap
    /// from an SKBitmap without an additional encode/decode step.
    /// </summary>
    public static RenderTargetBitmap ToRenderTargetBitmap(this SKBitmap source, double dpi = 96)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Derive PixelFormat / AlphaFormat from SKColorType
        var pixelFormat = source.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => PixelFormat.Bgra8888 // Fallback
        };

        var alphaFormat = source.AlphaType == SKAlphaType.Opaque
            ? AlphaFormat.Opaque
            : AlphaFormat.Unpremul;

        // SKBitmap pixel buffer “wrap” (no copy)
        // Bitmap() constructor accepts an IntPtr on raw data + stride
        // Source: Avalonia API documentation of the bitmap class :contentReference[oaicite:0]{index=0}
        using var avBitmap = new Bitmap(
            pixelFormat,
            alphaFormat,
            source.GetPixels(),
            new PixelSize(source.Width, source.Height),
            new Vector(dpi, dpi),
            source.RowBytes);

        // Create and draw RenderTargetBitmap
        var rtb = new RenderTargetBitmap(
            new PixelSize(source.Width, source.Height),
            new Vector(dpi, dpi));

        using var ctx = rtb.CreateDrawingContext(true);

        ctx.DrawImage(
            avBitmap,
            new Rect(0, 0, source.Width, source.Height));

        return rtb;
    }

    public static SKBitmap ToSKBitmap(this Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var skBitmap = new SKBitmap(bitmap.PixelSize.Width, bitmap.PixelSize.Height);

        // Kopiere die Pixeldaten in das SKBitmap
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream);
        memoryStream.Position = 0;

        using var skData = SKData.CreateCopy(memoryStream.ToArray());
        using var skImage = SKImage.FromEncodedData(skData);
        skImage.ReadPixels(skBitmap.Info, skBitmap.GetPixels());

        return skBitmap;
    }

    public static SKColor ToSKColor(this Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    public static RenderTargetBitmap RenderTextToBitmap(string text, int imageWidth, int imageHeight)
    {
        // Create an SKBitmap for rendering
        var bitmap = new SKBitmap(imageWidth, imageHeight);
        using var canvas = new SKCanvas(bitmap);

        // Set black background
        canvas.Clear(SKColors.Black);

        // Draw text
        DrawTextAt(
            canvas,
            text,
            SKColors.White,
            14,
            true,
            0,
            0,
            imageWidth,
            imageHeight
        );

        // Convert SKBitmap to RenderTargetBitmap
        return bitmap.ToRenderTargetBitmap();
    }

    /// <summary>
    /// Draws text at the given position in the specified DrawingContext.
    /// </summary>
    private static void DrawTextAt(
        SKCanvas canvas,
        string text,
        SKColor color,
        float textSize,
        bool centered,
        float posX = 0,
        float posY = 0,
        float imageWidth = 90,
        float imageHeight = 90,
        bool bold = false,
        bool italic = false,
        bool outlined = false,
        SKColor outlineColor = default)
    {
        if (canvas == null || string.IsNullOrEmpty(text))
            throw new ArgumentException("Canvas oder Text dürfen nicht null sein!");

        using var textPaint = new SKPaint();
        
        textPaint.Color = color;
        textPaint.TextSize = textSize;
        textPaint.Style = SKPaintStyle.Fill;
        textPaint.TextAlign = centered ? SKTextAlign.Center : SKTextAlign.Left;
        textPaint.TextEncoding = SKTextEncoding.Utf32; // Better Unicode support
        textPaint.IsAntialias = true;
        textPaint.SubpixelText = true; // Improves text sharpness
        textPaint.LcdRenderText = true; // Optimized for LCD
        textPaint.HintingLevel = SKPaintHinting.Full; // Maximum font hinting
        textPaint.FilterQuality = SKFilterQuality.High; // Highest Render Quality
        textPaint.StrokeJoin = SKStrokeJoin.Round; // Improves the corners
        textPaint.StrokeCap = SKStrokeCap.Round;    // Improves endpoints
        textPaint.IsLinearText = false;
        
        textPaint.Typeface = SKTypeface.FromFamilyName(
            "Liberation Sans",
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright
        );

        // Split text into lines based on available width
        var lines = WrapText(text, textPaint, imageWidth);
        var lineHeight = textPaint.FontSpacing;
        var totalHeight = lineHeight * lines.Count;

        float startY;
        if (centered)
        {
            startY = posY + (imageHeight - totalHeight) / 2 + textPaint.TextSize;
        }
        else
        {
            startY = posY + textPaint.TextSize;
        }

        // Draw every line
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var drawX = centered ? imageWidth / 2 : posX;
            var drawY = startY + (i * lineHeight);

            if (outlined)
            {
                using var outlinePaint = new SKPaint();
                
                outlinePaint.Color = outlineColor;
                outlinePaint.TextSize = textSize;
                outlinePaint.Style = SKPaintStyle.Stroke;
                outlinePaint.TextAlign = textPaint.TextAlign;
                outlinePaint.Typeface = textPaint.Typeface;
                outlinePaint.StrokeWidth = 3;
                outlinePaint.IsAntialias = true;
                outlinePaint.SubpixelText = true; // Improves text sharpness
                outlinePaint.LcdRenderText = true; // Optimized for LCD
                outlinePaint.HintingLevel = SKPaintHinting.Full; // Maximum font hinting
                outlinePaint.FilterQuality = SKFilterQuality.High; // Highest Render Quality
                outlinePaint.StrokeJoin = SKStrokeJoin.Round; // Verbessert die Ecken
                outlinePaint.StrokeCap = SKStrokeCap.Round;    // Verbessert die Endpunkte
                outlinePaint.IsLinearText = false;

                canvas.DrawText(line, drawX, drawY, outlinePaint);
            }

            canvas.DrawText(line, drawX, drawY, textPaint);
        }
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            var testLine = currentLine.Length == 0 ? word : currentLine + " " + word;
            var testWidth = paint.MeasureText(testLine);

            if (testWidth <= maxWidth)
            {
                currentLine.Append(currentLine.Length == 0 ? word : " " + word);
            }
            else
            {
                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                }

                // If a single word is too long, break it down
                if (paint.MeasureText(word) > maxWidth)
                {
                    var chars = word.ToCharArray();
                    currentLine.Clear();
                    foreach (var c in chars)
                    {
                        var testChar = currentLine.ToString() + c;
                        if (paint.MeasureText(testChar) <= maxWidth)
                        {
                            currentLine.Append(c);
                        }
                        else
                        {
                            lines.Add(currentLine.ToString());
                            currentLine.Clear();
                            currentLine.Append(c);
                        }
                    }
                }
                else
                {
                    currentLine.Append(word);
                }
            }
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines;
    }

    public static Bitmap CloneBitmap(this Bitmap original)
    {
        if (original == null)
            return null;

        using var memoryStream = new MemoryStream();
        original.Save(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }

    public static RenderTargetBitmap CreateRenderTargetBitmap(Bitmap source)
    {
        var rtb = new RenderTargetBitmap(
            new PixelSize(source.PixelSize.Width, source.PixelSize.Height)
        );

        using var ctx = rtb.CreateDrawingContext();

        var destRect = new Rect(0, 0, rtb.PixelSize.Width, rtb.PixelSize.Height);
        var sourceRect = new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
        ctx.DrawImage(source, sourceRect, destRect);

        return rtb;
    }
}