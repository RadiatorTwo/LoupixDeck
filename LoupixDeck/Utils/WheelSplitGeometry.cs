using LoupixDeck.Models;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Geometry of a split Loupedeck CT wheel screen (<see cref="WheelSplitMode"/>): how many
/// segments a mode has, which segment a touch lands in, the area each segment draws in, and the
/// divider lines between them. All coordinates are in wheel-screen pixels, with a square screen of
/// <c>size</c> pixels whose visible area is the inscribed circle.
/// <para>Segment order:
/// <list type="bullet">
/// <item><see cref="WheelSplitMode.Horizontal"/>: 0 top, 1 bottom.</item>
/// <item><see cref="WheelSplitMode.Vertical"/>: 0 left, 1 right.</item>
/// <item><see cref="WheelSplitMode.Diagonal"/>: 0 top, 1 right, 2 bottom, 3 left.</item>
/// <item><see cref="WheelSplitMode.Cross"/>: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right.</item>
/// </list></para>
/// </summary>
public static class WheelSplitGeometry
{
    /// <summary>Largest segment count of any mode.</summary>
    public const int MaxSegments = 4;

    /// <summary>Number of segments of <paramref name="mode"/>; 0 for <see cref="WheelSplitMode.None"/>.</summary>
    public static int SegmentCount(WheelSplitMode mode) => mode switch
    {
        WheelSplitMode.Horizontal or WheelSplitMode.Vertical => 2,
        WheelSplitMode.Diagonal or WheelSplitMode.Cross => 4,
        _ => 0
    };

    /// <summary>
    /// The segment the point (<paramref name="x"/>, <paramref name="y"/>) lies in, or -1 for an
    /// unsplit mode. A point exactly on a divider goes to the later segment.
    /// </summary>
    public static int HitTest(WheelSplitMode mode, int x, int y, int size)
    {
        float c = size / 2f;
        float dx = x - c;
        float dy = y - c;

        return mode switch
        {
            WheelSplitMode.Horizontal => dy < 0 ? 0 : 1,
            WheelSplitMode.Vertical => dx < 0 ? 0 : 1,
            WheelSplitMode.Diagonal => Math.Abs(dy) >= Math.Abs(dx)
                ? (dy < 0 ? 0 : 2)
                : (dx > 0 ? 1 : 3),
            WheelSplitMode.Cross => (dy < 0 ? 0 : 2) + (dx < 0 ? 0 : 1),
            _ => -1
        };
    }

    /// <summary>
    /// The area segment <paramref name="index"/> draws in, as a polygon over the whole square
    /// screen (the glass hides everything outside the circle anyway). The caller disposes it.
    /// </summary>
    public static SKPath SegmentPath(WheelSplitMode mode, int index, int size)
    {
        float s = size;
        float c = s / 2f;
        var path = new SKPath();

        switch (mode)
        {
            case WheelSplitMode.Horizontal:
                path.AddRect(index == 0 ? new SKRect(0, 0, s, c) : new SKRect(0, c, s, s));
                break;
            case WheelSplitMode.Vertical:
                path.AddRect(index == 0 ? new SKRect(0, 0, c, s) : new SKRect(c, 0, s, s));
                break;
            case WheelSplitMode.Diagonal:
                SKPoint centre = new(c, c);
                SKPoint[] corners = index switch
                {
                    0 => [new SKPoint(0, 0), new SKPoint(s, 0)],
                    1 => [new SKPoint(s, 0), new SKPoint(s, s)],
                    2 => [new SKPoint(s, s), new SKPoint(0, s)],
                    _ => [new SKPoint(0, s), new SKPoint(0, 0)]
                };
                path.AddPoly([corners[0], corners[1], centre]);
                break;
            case WheelSplitMode.Cross:
                float left = (index % 2) == 0 ? 0 : c;
                float top = index < 2 ? 0 : c;
                path.AddRect(new SKRect(left, top, left + c, top + c));
                break;
            default:
                path.AddRect(new SKRect(0, 0, s, s));
                break;
        }

        return path;
    }

    /// <summary>
    /// Where content of segment <paramref name="index"/> sits best: a point inside the visible
    /// part of the segment, near the centroid of its slice of the circle. The screen centre for an
    /// unsplit mode.
    /// </summary>
    public static SKPoint Centroid(WheelSplitMode mode, int index, int size)
    {
        float c = size / 2f;
        float r = size / 2f;

        // Offsets from the centre, as a share of the radius: a bit past the area centroid of a
        // half (0.42 r) and a quarter slice (0.6 r), pulled in so a label box clears the rim.
        float half = r * 0.45f;
        float wedge = r * 0.55f;
        float quad = r * 0.4f;

        return mode switch
        {
            WheelSplitMode.Horizontal => new SKPoint(c, index == 0 ? c - half : c + half),
            WheelSplitMode.Vertical => new SKPoint(index == 0 ? c - half : c + half, c),
            WheelSplitMode.Diagonal => index switch
            {
                0 => new SKPoint(c, c - wedge),
                1 => new SKPoint(c + wedge, c),
                2 => new SKPoint(c, c + wedge),
                _ => new SKPoint(c - wedge, c)
            },
            WheelSplitMode.Cross => new SKPoint(
                (index % 2) == 0 ? c - quad : c + quad,
                index < 2 ? c - quad : c + quad),
            _ => new SKPoint(c, c)
        };
    }

    /// <summary>The divider lines of <paramref name="mode"/>, as (from, to) pairs. Empty when unsplit.</summary>
    public static IReadOnlyList<(SKPoint From, SKPoint To)> Dividers(WheelSplitMode mode, int size)
    {
        float s = size;
        float c = s / 2f;
        (SKPoint, SKPoint) horizontal = (new SKPoint(0, c), new SKPoint(s, c));
        (SKPoint, SKPoint) vertical = (new SKPoint(c, 0), new SKPoint(c, s));

        return mode switch
        {
            WheelSplitMode.Horizontal => [horizontal],
            WheelSplitMode.Vertical => [vertical],
            WheelSplitMode.Diagonal => [(new SKPoint(0, 0), new SKPoint(s, s)), (new SKPoint(s, 0), new SKPoint(0, s))],
            WheelSplitMode.Cross => [horizontal, vertical],
            _ => []
        };
    }

    /// <summary>Strokes the divider lines of <paramref name="mode"/> onto <paramref name="canvas"/>.</summary>
    public static void DrawDividers(SKCanvas canvas, WheelSplitMode mode, int size, SKColor color, float width)
    {
        var lines = Dividers(mode, size);
        if (lines.Count == 0) return;

        using var paint = new SKPaint
        {
            Color = color, StrokeWidth = width, IsAntialias = true, Style = SKPaintStyle.Stroke
        };
        foreach (var (from, to) in lines)
            canvas.DrawLine(from, to, paint);
    }

    /// <summary>Localization key of segment <paramref name="index"/>'s name, e.g. "Top".</summary>
    public static string SegmentNameKey(WheelSplitMode mode, int index) => mode switch
    {
        WheelSplitMode.Horizontal => index == 0 ? "Wheel_SegmentTop" : "Wheel_SegmentBottom",
        WheelSplitMode.Vertical => index == 0 ? "Wheel_SegmentLeft" : "Wheel_SegmentRight",
        WheelSplitMode.Diagonal => index switch
        {
            0 => "Wheel_SegmentTop",
            1 => "Wheel_SegmentRight",
            2 => "Wheel_SegmentBottom",
            _ => "Wheel_SegmentLeft"
        },
        WheelSplitMode.Cross => index switch
        {
            0 => "Wheel_SegmentTopLeft",
            1 => "Wheel_SegmentTopRight",
            2 => "Wheel_SegmentBottomLeft",
            _ => "Wheel_SegmentBottomRight"
        },
        _ => "TouchButton_CentreWheelTitle"
    };
}
