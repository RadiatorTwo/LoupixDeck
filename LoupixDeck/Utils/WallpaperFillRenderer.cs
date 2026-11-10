using LoupixDeck.Models;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Paints a <see cref="WallpaperFill"/> into a bitmap of the requested surface size. The result is
/// baked into the slot's cache by <see cref="BitmapHelper.GetOrBakeSlot"/>, the same way a scaled
/// image is, so every renderer draws it without knowing it was generated.
/// </summary>
public static class WallpaperFillRenderer
{
    // Fixed so the grain is identical on every bake: a re-bake after an unrelated change must not
    // make the background shimmer.
    private const uint NoiseSeed = 0x9E3779B9;

    // Largest per-channel offset at full noise strength. Enough to dissolve the 16 red levels of an
    // RGB565 panel into grain without making the background look dirty.
    private const int MaxNoiseAmplitude = 24;

    public static SKBitmap Bake(WallpaperFill fill, int width, int height)
    {
        if (fill == null || width <= 0 || height <= 0) return null;

        SKBitmap bitmap = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            SKRect bounds = new(0, 0, width, height);
            using (SKPaint paint = new() { IsAntialias = true, IsDither = true })
            {
                paint.Shader = CreateShader(fill, bounds);
                if (paint.Shader == null)
                    paint.Color = fill.StartColor.ToSKColor().WithAlpha(255);
                canvas.DrawRect(bounds, paint);
                paint.Shader?.Dispose();
            }

            if (fill.Vignette > 0)
                DrawVignette(canvas, bounds, fill.Vignette);

            canvas.Flush();
        }

        if (fill.Noise > 0)
            ApplyNoise(bitmap, fill.Noise);

        return bitmap;
    }

    private static SKShader CreateShader(WallpaperFill fill, SKRect bounds)
    {
        if (fill.Mode == WallpaperFillMode.Solid) return null;

        SKColor[] colors = fill.UseMidColor
            ? [Opaque(fill.StartColor), Opaque(fill.MidColor), Opaque(fill.EndColor)]
            : [Opaque(fill.StartColor), Opaque(fill.EndColor)];

        if (fill.Mode == WallpaperFillMode.Radial)
        {
            SKPoint center = new(
                bounds.Left + ((float)(fill.CenterX / 100.0) * bounds.Width),
                bounds.Top + ((float)(fill.CenterY / 100.0) * bounds.Height));
            float halfDiagonal = 0.5f * MathF.Sqrt((bounds.Width * bounds.Width) + (bounds.Height * bounds.Height));
            float radius = Math.Max(1f, halfDiagonal * (float)(fill.Radius / 100.0));
            return SKShader.CreateRadialGradient(center, radius, colors, null, SKShaderTileMode.Clamp);
        }

        // Linear: the gradient line runs through the centre and is just long enough for both ends to
        // touch the surface's corners, the same geometry the symbol-layer gradient uses.
        double rad = fill.Angle * Math.PI / 180.0;
        float dx = (float)Math.Cos(rad);
        float dy = (float)Math.Sin(rad);
        float half = 0.5f * ((Math.Abs(dx) * bounds.Width) + (Math.Abs(dy) * bounds.Height));
        if (half <= 0) return null;

        SKPoint mid = new(bounds.MidX, bounds.MidY);
        SKPoint p0 = new(mid.X - (dx * half), mid.Y - (dy * half));
        SKPoint p1 = new(mid.X + (dx * half), mid.Y + (dy * half));
        return SKShader.CreateLinearGradient(p0, p1, colors, null, SKShaderTileMode.Clamp);
    }

    private static SKColor Opaque(Avalonia.Media.Color color) => color.ToSKColor().WithAlpha(255);

    /// <summary>Darkens towards the edges: clear in the middle, black at the corners.</summary>
    private static void DrawVignette(SKCanvas canvas, SKRect bounds, double strength)
    {
        float radius = 0.5f * MathF.Sqrt((bounds.Width * bounds.Width) + (bounds.Height * bounds.Height));
        byte alpha = (byte)Math.Clamp(255 * strength, 0, 255);

        // Transparent *black*: SKColors.Transparent is transparent white, and interpolating from it
        // would lighten a ring before the edge darkens.
        SKColor clear = new(0, 0, 0, 0);
        using SKShader shader = SKShader.CreateRadialGradient(
            new SKPoint(bounds.MidX, bounds.MidY), radius,
            [clear, clear, new SKColor(0, 0, 0, alpha)],
            [0f, 0.45f, 1f],
            SKShaderTileMode.Clamp);
        using SKPaint paint = new() { Shader = shader, IsAntialias = true };
        canvas.DrawRect(bounds, paint);
    }

    /// <summary>Adds a fixed, monochrome grain so smooth ramps do not band on the 16-bit panel.</summary>
    private static void ApplyNoise(SKBitmap bitmap, double strength)
    {
        int amplitude = (int)Math.Round(MaxNoiseAmplitude * Math.Clamp(strength, 0, 1));
        if (amplitude <= 0) return;

        Span<byte> pixels = bitmap.GetPixelSpan();
        int count = bitmap.Width * bitmap.Height;
        uint state = NoiseSeed;

        for (int i = 0; i < count; i++)
        {
            // xorshift32: cheap, deterministic and plenty random for grain.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int offset = (int)(state % (uint)((2 * amplitude) + 1)) - amplitude;

            int p = i * 4;
            pixels[p] = Clamp(pixels[p] + offset);
            pixels[p + 1] = Clamp(pixels[p + 1] + offset);
            pixels[p + 2] = Clamp(pixels[p + 2] + offset);
        }

        bitmap.NotifyPixelsChanged();
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
