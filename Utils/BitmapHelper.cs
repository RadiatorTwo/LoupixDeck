using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LoupixDeck.Models;

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

        // Radii for the inner ring
        var innerRadiusX = (width - 2 * innerRingMargin) / 2.0;
        var innerRadiusY = (height - 2 * innerRingMargin) / 2.0;

        var innerRingPen = new Pen(brush, innerRingThickness);

        // We have no DrawArc, so we need to draw it with geometry ourselve
        var geometry = new StreamGeometry();
        using (var geoCtx = geometry.Open())
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
        ctx.DrawGeometry(Brushes.Transparent, innerRingPen, geometry);

        return rtb;
    }

    /// <summary>
    /// Renders the content of a TouchButton (background, image, text) into an Avalonia bitmap.
    /// </summary>
    public static RenderTargetBitmap RenderTouchButtonContent(TouchButton touchButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height)
        );

        using var ctx = rtb.CreateDrawingContext(true);

        var backgroundBrush = new ImmutableSolidColorBrush(touchButton.BackColor);
        ctx.DrawRectangle(
            backgroundBrush,
            pen: null,
            rect: new Rect(0, 0, width, height)
        );

        if (touchButton.Image != null)
        {
            var imageWidth = touchButton.Image.PixelSize.Width;
            var imageHeight = touchButton.Image.PixelSize.Height;

            var sourceRect = new Rect(0, 0, imageWidth, imageHeight);

            // Calculate the ratio to scale the image to the target size
            var scaleX = width / (double)imageWidth;
            var scaleY = height / (double)imageHeight;

            // Select the smaller ratio to fit the image completely without distortion
            var baseScale = Math.Min(scaleX, scaleY);

            // Applying scaling using ImageScale
            var scaleFactor = Math.Max(0.01, touchButton.ImageScale / 100.0);
            var finalScale = baseScale * scaleFactor;

            // New width and height after scaling (aspect ratio is retained)
            var scaledWidth = imageWidth * finalScale;
            var scaledHeight = imageHeight * finalScale;

            // Centre the image if it is smaller than the target image
            var posX = touchButton.ImagePositionX;
            var posY = touchButton.ImagePositionY;

            if (posX == 0 && posY == 0) // Standard: Set image in the centre if no position is specified
            {
                posX = (int)((width - scaledWidth) / 2);
                posY = (int)((height - scaledHeight) / 2);
            }

            var destRect = new Rect(posX, posY, scaledWidth, scaledHeight);

            ctx.DrawImage(touchButton.Image, sourceRect, destRect);
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