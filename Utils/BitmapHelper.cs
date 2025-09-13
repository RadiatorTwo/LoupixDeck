using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LoupixDeck.Models;
using SkiaSharp;

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
    public static SKBitmap RenderTouchButtonContent(
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
            var wallpaperBitmap = config.Wallpaper;
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
            var destRect = new SKRect(0, 0, width, height);
            
            var scaledImage = BitmapHelper.ScaleAndPositionBitmap(
                touchButton.Image,
                width,
                height,
                touchButton.ImageScale,
                touchButton.ImagePositionX,
                touchButton.ImagePositionY);
            
            canvas.DrawBitmap(scaledImage, destRect);
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
        // var rtb = bitmap.ToRenderTargetBitmap();
        touchButton.RenderedImage = bitmap;

        return bitmap;
    }

    /// <summary>
    /// Scales and positions a bitmap and returns the result as a new SKBitmap.
    /// </summary>
    public static SKBitmap ScaleAndPositionBitmap(
        SKBitmap source,
        int targetWidth,
        int targetHeight,
        float imageScale = 100f,
        int posX = 0,
        int posY = 0,
        ScalingOption scalingOption = ScalingOption.Fit)
    {
        ArgumentNullException.ThrowIfNull(source);

        // ---------- 1) Basic size after scaling (without imageScale) --------
        float baseW = source.Width;
        float baseH = source.Height;

        switch (scalingOption)
        {
            case ScalingOption.Fit:
            {
                var f = Math.Min(targetWidth / baseW, targetHeight / baseH);
                baseW *= f;
                baseH *= f;
                break;
            }
            case ScalingOption.Fill:
            {
                var f = Math.Max(targetWidth / baseW, targetHeight / baseH);
                baseW *= f;
                baseH *= f;
                break;
            }
            case ScalingOption.Stretch:
                baseW = targetWidth;
                baseH = targetHeight;
                break;
            case ScalingOption.None:
            case ScalingOption.Center:
            case ScalingOption.Tile:
                // keine Änderung
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scalingOption), scalingOption, null);
        }

        // ---------- 2) imageScale as the final stage -----------------------------
        var scale = Math.Max(0.01f, imageScale / 100f);
        var dstW = Math.Max(1, (int)Math.Round(baseW * scale));
        var dstH = Math.Max(1, (int)Math.Round(baseH * scale));

        // ---------- 3) Sampler (Downscale = linear + MipMaps, Upscale = Biqubic Mitchell)  ------------------
        SKSamplingOptions sampling;

        if (scale > 1)
        {
            sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        }
        else
        {
            sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        }

        // ---------- 4) Bitmap (one-time) high-quality resampling ------------------
        using var scaledBmp = new SKBitmap(dstW, dstH, source.ColorType, source.AlphaType);
        source.ScalePixels(scaledBmp, sampling);

        // ---------- 5) Prepare target surface --------------------------------
        var dstInfo = new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType);
        var dst = new SKBitmap(dstInfo);
        dst.Erase(SKColors.Transparent);

        using var canvas = new SKCanvas(dst);

        // ---------- 6) Render paths ---------------------------------------------
        if (scalingOption == ScalingOption.Tile)
        {
            // *** Kachel-Shader: imageScale wirkt via scaledBmp-Größe ***
            var localMatrix = SKMatrix.CreateTranslation(-posX, -posY);

            using var shader = scaledBmp.ToShader(
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                sampling,
                localMatrix);

            using var p = new SKPaint();
            p.Shader = shader;

            canvas.DrawRect(new SKRect(0, 0, targetWidth, targetHeight), p);
        }
        else
        {
            // Single image
            float drawX = posX;
            float drawY = posY;

            if (scalingOption is ScalingOption.Center or ScalingOption.Fit or ScalingOption.Fill)
            {
                drawX += (targetWidth - dstW) * 0.5f;
                drawY += (targetHeight - dstH) * 0.5f;
            }

            var destRect = new SKRect(drawX, drawY, drawX + dstW, drawY + dstH);
            canvas.DrawBitmap(scaledBmp,
                new SKRect(0, 0, dstW, dstH), // Quelle 1:1
                destRect);
        }

        canvas.Flush();
        return dst;
    }


    public static SKColor ToSKColor(this Color color)
    {
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    public static SKBitmap RenderTextToBitmap(string text, int imageWidth, int imageHeight)
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
        return bitmap;
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

        var typeface = SKTypeface.FromFamilyName(
            "Liberation Sans",
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright
        );

        var font = new SKFont(typeface, textSize)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.Full
        };

        using var textPaint = new SKPaint();

        textPaint.Color = color;
        textPaint.Style = SKPaintStyle.Fill;
        textPaint.IsAntialias = true;
        textPaint.StrokeJoin = SKStrokeJoin.Round;
        textPaint.StrokeCap = SKStrokeCap.Round;

        // Split text into lines based on available width
        var lines = WrapText(text, font, imageWidth);
        var lineHeight = font.Spacing;
        var totalHeight = lineHeight * lines.Count;
        var startY = centered
            ? posY + (imageHeight - totalHeight) / 2 - font.Metrics.Ascent
            : posY - font.Metrics.Ascent;

        // Draw every line
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            var textWidth = font.MeasureText(line);
            var drawX = centered
                ? posX + (imageWidth - textWidth) / 2f
                : posX;

            var drawY = startY + (i * lineHeight);

            if (outlined)
            {
                using var outlinePaint = new SKPaint();

                outlinePaint.Color = outlineColor;
                outlinePaint.Style = SKPaintStyle.Stroke;
                outlinePaint.StrokeWidth = 3;
                outlinePaint.IsAntialias = true;
                outlinePaint.StrokeJoin = SKStrokeJoin.Round;
                outlinePaint.StrokeCap = SKStrokeCap.Round;

                canvas.DrawText(line, drawX, drawY, font, outlinePaint);
            }

            canvas.DrawText(line, drawX, drawY, font, textPaint);
        }
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            var testLine = currentLine.Length == 0 ? word : currentLine + " " + word;
            var testWidth = font.MeasureText(testLine);

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
                if (font.MeasureText(word) > maxWidth)
                {
                    var chars = word.ToCharArray();
                    currentLine.Clear();
                    foreach (var c in chars)
                    {
                        var testChar = currentLine.ToString() + c;
                        if (font.MeasureText(testChar) <= maxWidth)
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
}