using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using LoupixDeck.Models;
using System;
using System.Globalization;
using Color = SixLabors.ImageSharp.Color;
using SixLabors.ImageSharp.Drawing;
using System.Numerics;

namespace LoupixDeck.Utils;

public static class BitmapHelper
{
    public static Image<Rgba32> RenderSimpleButtonImage(SimpleButton simpleButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(simpleButton);

        var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx => ctx.Clear(Color.Transparent));

        const int ringThickness = 3;
        const int margin = 8;
        const int innerRingThickness = 4;
        const int innerRingMargin = 28;
        const double gapAngle = 45.0;
        const double startAngle = 60.0;

        var buttonColor = ConvertColor(simpleButton.ButtonColor);
        var center = new PointF(width / 2f, height / 2f);

        // Außenring (Ellipse)
        var outerRadiusX = (width - 2 * margin) / 2f;
        var outerRadiusY = (height - 2 * margin) / 2f;

        var unitCircle = new EllipsePolygon(0, 0, 1);

        var transform = Matrix3x2.CreateScale(outerRadiusX, outerRadiusY) * Matrix3x2.CreateTranslation(center.X, center.Y);

        var outerEllipse = unitCircle.Transform(transform);

        image.Mutate(ctx =>
        {
            ctx.Draw(buttonColor, ringThickness, outerEllipse);
        });

        // Innenbogen (Arc)
        var innerRadiusX = (width - 2 * innerRingMargin) / 2f;
        var innerRadiusY = (height - 2 * innerRingMargin) / 2f;

        //const double endAngle = startAngle + (360.0 - gapAngle);
        //const int segmentCount = 100;
        //var angleStep = (endAngle - startAngle) / segmentCount;

        var arcBounds = new RectangleF(
            center.X - innerRadiusX,
            center.Y - innerRadiusY,
            innerRadiusX * 2,
            innerRadiusY * 2
        );

        var pathBuilder = new PathBuilder();
        pathBuilder.StartFigure(); // Not actually needed, but seems cleaner
        pathBuilder.AddArc(arcBounds, 0, (float)startAngle, (float)(360 - gapAngle));

        var innerArc = pathBuilder.Build();

        image.Mutate(ctx =>
        {
            ctx.Draw(buttonColor, innerRingThickness, innerArc);
        });

        return image;
    }

    /// <summary>
    /// Renders the content of a TouchButton (background, image, text) into an Avalonia bitmap.
    /// </summary>
    public static Image<Rgba32> RenderTouchButtonContent(TouchButton touchButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        var image = new Image<Rgba32>(width, height);

        // Hintergrundfarbe
        image.Mutate(ctx => ctx.Fill(ConvertColor(touchButton.BackColor)));

        // Bild zeichnen
        if (touchButton.Image != null)
        {
            var img = touchButton.Image; // Das ist weiterhin Avalonia.Bitmap
            using var ms = new MemoryStream();
            img.Save(ms);
            ms.Position = 0;

            using var inputImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ms);

            var imageWidth = inputImage.Width;
            var imageHeight = inputImage.Height;

            var scaleX = width / (double)imageWidth;
            var scaleY = height / (double)imageHeight;
            var baseScale = Math.Min(scaleX, scaleY);
            var scaleFactor = Math.Max(0.01, touchButton.ImageScale / 100.0);
            var finalScale = baseScale * scaleFactor;

            var scaledWidth = (int)(imageWidth * finalScale);
            var scaledHeight = (int)(imageHeight * finalScale);

            var posX = touchButton.ImagePositionX;
            var posY = touchButton.ImagePositionY;

            if (posX == 0 && posY == 0)
            {
                posX = (int)((width - scaledWidth) / 2);
                posY = (int)((height - scaledHeight) / 2);
            }

            image.Mutate(ctx => ctx.DrawImage(inputImage, new Rectangle(posX, posY, scaledWidth, scaledHeight), 1f));
        }

        // Text zeichnen
        if (!string.IsNullOrEmpty(touchButton.Text))
        {
            DrawTextAt(
                image,
                touchButton.Text,
                ConvertColor(touchButton.TextColor),
                touchButton.TextSize,
                touchButton.TextCentered,
                touchButton.TextPositionX,
                touchButton.TextPositionY,
                width,
                height,
                touchButton.Bold,
                touchButton.Italic,
                touchButton.Outlined,
                ConvertColor(touchButton.OutlineColor)
            );
        }

        return image;
    }

    /// <summary>
    /// Draws text at the given position in the specified DrawingContext.
    /// </summary>
    private static void DrawTextAt(
        Image<Rgba32> image,
        string text,
        Color color,
        double textSize,
        bool centered,
        double posX,
        double posY,
        double imageWidth,
        double imageHeight,
        bool bold,
        bool italic,
        bool outlined,
        Color outlineColor)
    {
        var fontFamily = SystemFonts.Families.FirstOrDefault();

        var style = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular
        };

        var font = fontFamily.CreateFont((float)textSize, style);
        var textOptions = new TextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            WrappingLength = (float)imageWidth
        };

        var textSizeMeasured = TextMeasurer.MeasureBounds(text, textOptions);

        float drawX = (float)(centered ? (imageWidth - textSizeMeasured.Width) / 2 : posX);
        float drawY = (float)(centered ? (imageHeight - textSizeMeasured.Height) / 2 : posY);

        if (outlined)
        {
            const int offset = 1;
            var offsets = new (float dx, float dy)[]
            {
                (-offset, 0), (offset, 0), (0, -offset), (0, offset),
                (-offset, -offset), (offset, -offset), (-offset, offset), (offset, offset)
            };

            foreach (var (dx, dy) in offsets)
            {
                image.Mutate(ctx =>
                    ctx.DrawText(text, font, outlineColor, new PointF(drawX + dx, drawY + dy))
                );
            }
        }

        image.Mutate(ctx =>
            ctx.DrawText(text, font, color, new PointF(drawX, drawY))
        );
    }

    private static Color ConvertColor(Avalonia.Media.Color avaloniaColor)
    {
        return Color.FromRgba(avaloniaColor.R, avaloniaColor.G, avaloniaColor.B, avaloniaColor.A);
    }
}
