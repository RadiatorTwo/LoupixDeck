using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Diagnostics;

/// <summary>Which diagnostic image the display test draws.</summary>
public enum DisplayTestPattern
{
    /// <summary>Per-key frame, corner markers and slot number — key size and slot order.</summary>
    KeyGrid,

    /// <summary>Per-key pixel ruler counted from all four tile edges — visible pixel extent.</summary>
    Ruler,

    /// <summary>Per-key pure R/G/B/W quadrants plus a grey ramp — colour channel order.</summary>
    Color,

    /// <summary>Full-panel border, corner brackets and key grid lines — panel size and origin.</summary>
    Edges
}

/// <summary>
/// Draws the images behind the <c>System.DisplayTest</c> command. Every pattern is built
/// from the same numbers the real renderer uses (<see cref="Registry.DeviceGeometry"/>'s key
/// size, the device's column/row count, its framebuffer size), so a mismatch between what
/// the app believes the device looks like and what the panel actually shows becomes visible
/// as a clipped marker, a shifted frame or a swapped colour instead of a vague "looks wrong".
/// </summary>
public static class DisplayTestPatternRenderer
{
    private static readonly SKColor MarkerTopLeft = new(255, 0, 255); // magenta
    private static readonly SKColor MarkerTopRight = new(0, 255, 255); // cyan
    private static readonly SKColor MarkerBottomLeft = new(255, 255, 0); // yellow
    private static readonly SKColor MarkerBottomRight = new(0, 255, 0); // green

    private static readonly SKTypeface RegularFace = SKTypeface.FromFamilyName("Liberation Sans",
        SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

    private static readonly SKTypeface BoldFace = SKTypeface.FromFamilyName("Liberation Sans",
        SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

    /// <summary>
    /// True when the pattern is composed from key-sized tiles rather than from one
    /// full-panel image. Tile patterns are pushed through the device's normal per-slot
    /// compositing, so they exercise the exact offsets the page renderer uses.
    /// </summary>
    public static bool IsTilePattern(DisplayTestPattern pattern) => pattern != DisplayTestPattern.Edges;

    /// <summary>One key-sized tile for <paramref name="slot"/>. The caller owns the bitmap.</summary>
    public static SKBitmap RenderTile(DisplayTestPattern pattern, int slot, int columns, int keySize)
    {
        SKBitmap bitmap = new(new SKImageInfo(keySize, keySize, SKColorType.Bgra8888, SKAlphaType.Opaque));

        lock (SkiaRenderGate.Sync)
        {
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Black);

            switch (pattern)
            {
                case DisplayTestPattern.Ruler:
                    DrawRulerTile(canvas, keySize);
                    break;
                case DisplayTestPattern.Color:
                    DrawColorTile(canvas, keySize);
                    break;
                default:
                    DrawKeyGridTile(canvas, slot, columns, keySize);
                    break;
            }
        }

        return bitmap;
    }

    /// <summary>
    /// The full-panel image for <see cref="DisplayTestPattern.Edges"/>. The caller owns the bitmap.
    /// </summary>
    public static SKBitmap RenderPanel(int width, int height, int gridOriginX, int keySize, int columns, int rows,
        string deviceName)
    {
        SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));

        lock (SkiaRenderGate.Sync)
        {
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Black);
            DrawEdgesPanel(canvas, width, height, gridOriginX, keySize, columns, rows, deviceName);
        }

        return bitmap;
    }

    /// <summary>
    /// Per-key frame plus four differently coloured corner blocks and the slot number. A
    /// missing or clipped corner block means the panel shows less than one full key tile,
    /// and which corner is cut says in which direction. Numbers appearing out of reading
    /// order mean the slot-to-position mapping is wrong.
    /// </summary>
    private static void DrawKeyGridTile(SKCanvas canvas, int slot, int columns, int keySize)
    {
        int column = columns > 0 ? slot % columns : 0;
        int row = columns > 0 ? slot / columns : 0;

        // Checkerboard, so two adjacent tiles never share a background colour: the seam
        // stays visible even where the 1px frame itself is swallowed by a bezel.
        SKColor background = ((column + row) & 1) == 0 ? new SKColor(24, 24, 48) : new SKColor(48, 24, 24);
        canvas.Clear(background);

        using SKPaint stroke = new() { Color = SKColors.White, IsStroke = true, StrokeWidth = 1, IsAntialias = false };
        // Half-pixel inset so the 1px stroke lands exactly on the outermost pixel row.
        canvas.DrawRect(new SKRect(0.5f, 0.5f, keySize - 0.5f, keySize - 0.5f), stroke);

        int marker = Math.Max(4, keySize / 12);
        FillRect(canvas, 0, 0, marker, marker, MarkerTopLeft);
        FillRect(canvas, keySize - marker, 0, marker, marker, MarkerTopRight);
        FillRect(canvas, 0, keySize - marker, marker, marker, MarkerBottomLeft);
        FillRect(canvas, keySize - marker, keySize - marker, marker, marker, MarkerBottomRight);

        DrawCentredText(canvas, slot.ToString(), keySize / 2f, (keySize / 2f) + (keySize / 9f),
            keySize / 2.6f, SKColors.White, bold: true);
        DrawCentredText(canvas, $"{keySize}px", keySize / 2f, keySize - marker - 4f,
            Math.Max(8f, keySize / 9f), new SKColor(180, 180, 180), bold: false);
    }

    /// <summary>
    /// A pixel ruler counted inwards from every tile edge: white from the top and left,
    /// red from the bottom and right. The highest label still visible on each side is the
    /// number of pixels the panel really shows per key, and the side whose labels are
    /// missing is the side the pixels are cropped from.
    /// </summary>
    private static void DrawRulerTile(SKCanvas canvas, int keySize)
    {
        using SKPaint fine = new() { Color = new SKColor(90, 90, 90), IsAntialias = false };
        using SKPaint white = new() { Color = SKColors.White, IsAntialias = false };
        using SKPaint red = new() { Color = new SKColor(255, 60, 60), IsAntialias = false };

        float labelSize = Math.Max(7f, keySize / 12f);
        int majorLength = Math.Max(10, keySize / 6);
        SKColor redLabel = new(255, 60, 60);

        // The four tile edges themselves — a missing baseline is the fastest read of
        // "this side is cropped", before any tick or label is counted.
        canvas.DrawRect(0, 0, keySize, 1, white);
        canvas.DrawRect(0, 0, 1, keySize, white);
        canvas.DrawRect(0, keySize - 1, keySize, 1, red);
        canvas.DrawRect(keySize - 1, 0, 1, keySize, red);

        for (int offset = 0; offset < keySize; offset++)
        {
            bool major = (offset % 32) == 0;
            bool medium = (offset % 16) == 0;
            if (!major && !medium && (offset % 8) != 0) continue;

            int length = major ? majorLength : medium ? Math.Max(6, keySize / 10) : 3;
            SKPaint paint = major ? white : fine;
            SKPaint mirroredPaint = major ? red : fine;

            // Counted inwards from the top and the left edge.
            canvas.DrawRect(offset, 0, 1, length, paint);
            canvas.DrawRect(0, offset, length, 1, paint);

            // Counted inwards from the bottom and the right edge, in red.
            int mirrored = keySize - 1 - offset;
            canvas.DrawRect(mirrored, keySize - length, 1, length, mirroredPaint);
            canvas.DrawRect(keySize - length, mirrored, length, 1, mirroredPaint);

            if (!major || offset == 0) continue;

            // One label per ruler, each sitting next to the tick it belongs to, so a
            // cropped edge takes its own labels with it and leaves the others readable.
            DrawCentredText(canvas, offset.ToString(), offset, majorLength + labelSize,
                labelSize, SKColors.White, bold: false);
            DrawCentredText(canvas, offset.ToString(), majorLength + (labelSize * 0.8f),
                offset + (labelSize * 0.4f), labelSize, SKColors.White, bold: false);
            DrawCentredText(canvas, offset.ToString(), mirrored, keySize - majorLength - 3f,
                labelSize, redLabel, bold: false);
            DrawCentredText(canvas, offset.ToString(), keySize - majorLength - (labelSize * 0.8f),
                mirrored + (labelSize * 0.4f), labelSize, redLabel, bold: false);
        }

        // The extreme pixels of the tile. If these do not reach the glass the tile is
        // being cropped rather than merely offset.
        FillRect(canvas, 0, 0, 3, 3, MarkerTopLeft);
        FillRect(canvas, keySize - 3, keySize - 3, 3, 3, MarkerBottomRight);
        DrawCentredText(canvas, $"0-{keySize - 1}", keySize / 2f, keySize * 0.62f,
            Math.Max(9f, keySize / 8f), new SKColor(120, 220, 255), bold: true);
    }

    /// <summary>
    /// Pure red / green / blue / white quadrants, each labelled with its own letter. A
    /// quadrant whose colour does not match its label means the pixel byte order on the
    /// wire is wrong — the classic RGB-vs-BGR / RGB565 endianness swap shows up as red and
    /// blue trading places. The grey ramp along the bottom exposes quantisation banding.
    /// </summary>
    private static void DrawColorTile(SKCanvas canvas, int keySize)
    {
        int half = keySize / 2;
        int rampHeight = Math.Max(6, keySize / 10);

        FillRect(canvas, 0, 0, half, half, new SKColor(255, 0, 0));
        FillRect(canvas, half, 0, keySize - half, half, new SKColor(0, 255, 0));
        FillRect(canvas, 0, half, half, keySize - half, new SKColor(0, 0, 255));
        FillRect(canvas, half, half, keySize - half, keySize - half, SKColors.White);

        float labelSize = Math.Max(10f, keySize / 5f);
        float baselineShift = labelSize / 3f;
        DrawCentredText(canvas, "R", half / 2f, (half / 2f) + baselineShift, labelSize, SKColors.Black, bold: true);
        DrawCentredText(canvas, "G", half + (half / 2f), (half / 2f) + baselineShift, labelSize, SKColors.Black, bold: true);
        DrawCentredText(canvas, "B", half / 2f, half + (half / 2f) + baselineShift, labelSize, SKColors.White, bold: true);
        DrawCentredText(canvas, "W", half + (half / 2f), half + (half / 2f) + baselineShift, labelSize, SKColors.Black, bold: true);

        for (int x = 0; x < keySize; x++)
        {
            byte level = (byte)((x * 255) / Math.Max(1, keySize - 1));
            FillRect(canvas, x, keySize - rampHeight, 1, rampHeight, new SKColor(level, level, level));
        }
    }

    /// <summary>
    /// One image for the whole framebuffer: a 1px frame on the outermost pixel row, corner
    /// brackets on the extreme pixels, the key grid drawn where the app believes the keys
    /// are, and a centre crosshair. A cut-off frame means the panel is smaller than the
    /// buffer the app writes; an off-centre crosshair means the buffer is offset.
    /// </summary>
    private static void DrawEdgesPanel(SKCanvas canvas, int width, int height, int gridOriginX, int keySize,
        int columns, int rows, string deviceName)
    {
        using SKPaint frame = new() { Color = SKColors.White, IsStroke = true, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawRect(new SKRect(0.5f, 0.5f, width - 0.5f, height - 0.5f), frame);

        using SKPaint inner = new()
            { Color = new SKColor(255, 60, 60), IsStroke = true, StrokeWidth = 1, IsAntialias = false };
        canvas.DrawRect(new SKRect(4.5f, 4.5f, width - 4.5f, height - 4.5f), inner);

        const int arm = 24;
        FillRect(canvas, 0, 0, arm, 3, MarkerTopLeft);
        FillRect(canvas, 0, 0, 3, arm, MarkerTopLeft);
        FillRect(canvas, width - arm, 0, arm, 3, MarkerTopRight);
        FillRect(canvas, width - 3, 0, 3, arm, MarkerTopRight);
        FillRect(canvas, 0, height - 3, arm, 3, MarkerBottomLeft);
        FillRect(canvas, 0, height - arm, 3, arm, MarkerBottomLeft);
        FillRect(canvas, width - arm, height - 3, arm, 3, MarkerBottomRight);
        FillRect(canvas, width - 3, height - arm, 3, arm, MarkerBottomRight);

        // Where the app places the key boundaries on this panel.
        using SKPaint gridLine = new() { Color = new SKColor(0, 160, 255), IsAntialias = false };
        for (int column = 0; column <= columns; column++)
        {
            int x = gridOriginX + (column * keySize);
            if (x >= 0 && x < width) canvas.DrawRect(x, 0, 1, height, gridLine);
        }

        for (int row = 0; row <= rows; row++)
        {
            int y = row * keySize;
            if (y >= 0 && y < height) canvas.DrawRect(0, y, width, 1, gridLine);
        }

        float centreX = width / 2f;
        float centreY = height / 2f;
        using SKPaint cross = new() { Color = SKColors.White, IsStroke = true, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(centreX - 20, centreY, centreX + 20, centreY, cross);
        canvas.DrawLine(centreX, centreY - 20, centreX, centreY + 20, cross);
        canvas.DrawCircle(centreX, centreY, 12, cross);

        DrawCentredText(canvas, $"{width}x{height}", centreX, centreY - 26, 16f, SKColors.White, bold: true);
        DrawCentredText(canvas, $"{deviceName}  key {keySize}  grid x={gridOriginX}", centreX, centreY + 40, 12f,
            new SKColor(180, 180, 180), bold: false);
    }

    private static void FillRect(SKCanvas canvas, int x, int y, int width, int height, SKColor color)
    {
        using SKPaint paint = new() { Color = color, IsAntialias = false };
        canvas.DrawRect(x, y, width, height, paint);
    }

    private static void DrawCentredText(SKCanvas canvas, string text, float x, float baselineY, float size,
        SKColor color, bool bold)
    {
        using SKFont font = new(bold ? BoldFace : RegularFace, size) { Edging = SKFontEdging.Antialias };
        using SKPaint paint = new() { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, baselineY, SKTextAlign.Center, font, paint);
    }
}
