using System.Globalization;
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
        int width,
        int height,
        Bitmap wallpaper,
        int gridColumns = 0)
    {
        ArgumentNullException.ThrowIfNull(touchButton);


        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height)
        );

        using var ctx = rtb.CreateDrawingContext(true);

        if (wallpaper != null && gridColumns > 0)
        {
            // Determine the position of the button in the 5×3 grid
            // Provided you have a list of all TouchButtons somewhere:
            // var index = allButtons.IndexOf(touchButton);
            // Or: TouchButton has row/column properties ready.
            var col = touchButton.Index % gridColumns;
            var row = touchButton.Index / gridColumns;

            // Calculate the section from the wallpaper
            var srcRect = new Rect(
                x: col * width,
                y: row * height,
                width: width,
                height: height
            );

            // Draw this wallpaper slice first
            ctx.DrawImage(wallpaper, srcRect, new Rect(0, 0, width, height));

            // (Optional future feature) Add a semi-transparent background color
            //var backgroundBrush = new ImmutableSolidColorBrush(touchButton.BackColor);
            //ctx.DrawRectangle(backgroundBrush, null, new Rect(0,0,width,height));
        }
        else
        {
            var backgroundBrush = new ImmutableSolidColorBrush(touchButton.BackColor);
            ctx.DrawRectangle(
                backgroundBrush,
                pen: null,
                rect: new Rect(0, 0, width, height)
            );
        }

        if (touchButton.Image != null)
        {
            var destRect = new Rect(0, 0, width, height);
            ctx.DrawImage(touchButton.Image, destRect);
        }

        if (!string.IsNullOrEmpty(touchButton.Text))
        {
            DrawTextAt(
                ctx,
                touchButton.Text,
                touchButton.TextColor,
                touchButton.TextSize,
                touchButton.TextCentered,
                touchButton.TextPositionX,
                touchButton.TextPositionY,
                width,
                height,
                touchButton.Bold,
                touchButton.Italic,
                touchButton.Outlined,
                touchButton.OutlineColor
            );
        }

        touchButton.RenderedImage = rtb;

        return rtb;
    }

    /// <summary>
    /// Scales and positions a bitmap in the same way as RenderTouchButtonContent
    /// and returns the result as a new SKBitmap.
    /// </summary>
    public static SKBitmap ScaleAndPositionBitmap(
        SKBitmap source,
        int targetWidth,
        int targetHeight,
        double imageScale,
        int posX,
        int posY)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Create target bitmap (transparent background)
        var result = new SKBitmap(targetWidth, targetHeight, source.ColorType, source.AlphaType);

        // Calculate scaling
        var scaleX = (double)targetWidth / source.Width;
        var scaleY = (double)targetHeight / source.Height;
        var baseScale = Math.Min(scaleX, scaleY); // Bild vollständig einpassen
        var scaleFactor = Math.Max(0.01, imageScale / 100.0); // 0,01 = Sicherheitsminimum
        var finalScale = baseScale * scaleFactor;

        var scaledW = (float)(source.Width * finalScale);
        var scaledH = (float)(source.Height * finalScale);

        // Calculate position (0/0 ⇒ centered)
        if (posX == 0 && posY == 0)
        {
            posX = (int)((targetWidth - scaledW) / 2);
            posY = (int)((targetHeight - scaledH) / 2);
        }

        var destRect = new SKRect(
            posX,
            posY,
            posX + scaledW,
            posY + scaledH);

        using var canvas = new SKCanvas(result);

        var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true,
            IsDither = true
        };

        canvas.Clear(SKColors.Transparent);

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

    public static RenderTargetBitmap RenderTextToBitmap(string text, int imageWidth, int imageHeight)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(imageWidth, imageHeight));

        using var ctx = rtb.CreateDrawingContext(true);

        ctx.DrawRectangle(
            brush: Brushes.Black,
            pen: null,
            rect: new Rect(0, 0, imageWidth, imageHeight)
        );

        DrawTextAt(
            ctx,
            text,
            Colors.White,
            14,
            true,
            0,
            0,
            imageWidth,
            imageHeight
        );

        return rtb;
    }

    /// <summary>
    /// Draws text at the given position in the specified DrawingContext.
    /// </summary>
    private static void DrawTextAt(
        DrawingContext context,
        string text,
        Color color,
        double textSize,
        bool centered,
        double posX = 0,
        double posY = 0,
        double imageWidth = 90,
        double imageHeight = 90,
        bool bold = false,
        bool italic = false,
        bool outlined = false,
        Color outlineColor = default)
    {
        if (context == null || string.IsNullOrEmpty(text))
            throw new ArgumentException("The drawing context or text must not be null!");

        var brush = new ImmutableSolidColorBrush(color);

        var typeface = new Typeface(
            FontFamily.Default,
            italic ? FontStyle.Italic : FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal
        );

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            textSize,
            brush
        )
        {
            TextAlignment = TextAlignment.Left,
            MaxTextHeight = 85,
            MaxTextWidth = 85
        };

        var textWidth = formattedText.Width;
        var textHeight = formattedText.Height;

        int drawX;
        int drawY;

        if (centered)
        {
            var centerX = imageWidth / 2;
            var centerY = imageHeight / 2;

            drawX = (int)Math.Round(centerX - (textWidth / 2));
            drawY = (int)Math.Round(centerY - (textHeight / 2));
        }
        else
        {
            drawX = (int)Math.Round(posX);
            drawY = (int)Math.Round(posY);
        }

        if (outlined)
        {
            var outlineBrush = new ImmutableSolidColorBrush(outlineColor);
            const int outlineOffset = 1;

            var outlineText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                textSize,
                outlineBrush
            )
            {
                TextAlignment = TextAlignment.Left,
                MaxTextHeight = 85,
                MaxTextWidth = 85
            };

            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY));
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY));
            context.DrawText(outlineText, new Point(drawX, drawY - outlineOffset));
            context.DrawText(outlineText, new Point(drawX, drawY + outlineOffset));
            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY - outlineOffset));
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY - outlineOffset));
            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY + outlineOffset));
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY + outlineOffset));
        }

        context.DrawText(formattedText, new Point(drawX, drawY));
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