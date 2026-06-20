using LoupixDeck.PluginSdk;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Host implementation of <see cref="IRenderCanvas"/> over a SkiaSharp <see cref="SKCanvas"/>.
/// Plugins draw onto this with host primitives (text/symbols via <see cref="BitmapHelper"/>, so
/// fonts/symbols match the core); <see cref="DrawImage(byte[],int,int,int,int)"/> composites
/// plugin-rasterized bytes.
///
/// <para>Wraps a region of the underlying canvas at (<c>originX</c>,<c>originY</c>) sized
/// <see cref="Width"/>×<see cref="Height"/> — e.g. one 60×90 strip segment inside the 60×270 strip
/// canvas. Region-local coordinates are translated by the origin, then by the plugin's transform
/// stack, and clipped to the region. The caller must already hold <see cref="SkiaRenderGate"/>.Sync;
/// this type does not re-lock.</para>
/// </summary>
internal sealed class SkiaRenderCanvas : IRenderCanvas
{
    private readonly SKCanvas _canvas;
    private readonly float _originX;
    private readonly float _originY;

    // Plugin-controlled transform applied inside the region (after origin + clip).
    private SKMatrix _transform = SKMatrix.CreateIdentity();
    private readonly Stack<SKMatrix> _transformStack = new();

    public SkiaRenderCanvas(SKCanvas canvas, int width, int height, float originX = 0, float originY = 0)
    {
        _canvas = canvas;
        Width = width;
        Height = height;
        _originX = originX;
        _originY = originY;
    }

    public int Width { get; }
    public int Height { get; }

    private static SKColor ToSk(PluginColor c) => new(c.R, c.G, c.B, c.A);

    /// <summary>Runs <paramref name="draw"/> with the region placed at its origin, clipped to the
    /// region rect, and the plugin transform applied — then restores the canvas so the shared
    /// strip canvas is untouched.</summary>
    private void InRegion(Action draw)
    {
        var saved = _canvas.Save();
        try
        {
            _canvas.Translate(_originX, _originY);
            _canvas.ClipRect(new SKRect(0, 0, Width, Height));
            var t = _transform;
            _canvas.Concat(ref t);
            draw();
        }
        finally
        {
            _canvas.RestoreToCount(saved);
        }
    }

    public void Clear(PluginColor color) =>
        InRegion(() =>
        {
            using var paint = new SKPaint { Color = ToSk(color), Style = SKPaintStyle.Fill, IsAntialias = false };
            _canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
        });

    // ── Rectangles ──────────────────────────────────────────────────────────
    public void FillRectangle(int x, int y, int width, int height, PluginColor color) =>
        InRegion(() => { using var p = Fill(color); _canvas.DrawRect(new SKRect(x, y, x + width, y + height), p); });

    public void DrawRectangle(int x, int y, int width, int height, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawRect(new SKRect(x, y, x + width, y + height), p); });

    public void FillRoundedRectangle(int x, int y, int width, int height, int radius, PluginColor color) =>
        InRegion(() => { using var p = Fill(color); _canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, p); });

    public void DrawRoundedRectangle(int x, int y, int width, int height, int radius, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, p); });

    // ── Circles / ellipses / arcs ───────────────────────────────────────────
    public void FillCircle(int centerX, int centerY, int radius, PluginColor color) =>
        InRegion(() => { using var p = Fill(color); _canvas.DrawCircle(centerX, centerY, radius, p); });

    public void DrawCircle(int centerX, int centerY, int radius, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawCircle(centerX, centerY, radius, p); });

    public void FillEllipse(int x, int y, int width, int height, PluginColor color) =>
        InRegion(() => { using var p = Fill(color); _canvas.DrawOval(new SKRect(x, y, x + width, y + height), p); });

    public void DrawEllipse(int x, int y, int width, int height, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawOval(new SKRect(x, y, x + width, y + height), p); });

    public void DrawArc(int x, int y, int width, int height, float startAngle, float sweepAngle, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawArc(new SKRect(x, y, x + width, y + height), startAngle, sweepAngle, false, p); });

    public void FillArc(int x, int y, int width, int height, float startAngle, float sweepAngle, PluginColor color) =>
        InRegion(() => { using var p = Fill(color); _canvas.DrawArc(new SKRect(x, y, x + width, y + height), startAngle, sweepAngle, true, p); });

    // ── Lines ───────────────────────────────────────────────────────────────
    public void DrawLine(int x1, int y1, int x2, int y2, int strokeWidth, PluginColor color) =>
        InRegion(() => { using var p = Stroke(color, strokeWidth); _canvas.DrawLine(x1, y1, x2, y2, p); });

    // ── Text ────────────────────────────────────────────────────────────────
    public void DrawText(string text, int x, int y, int width, int height, PluginColor color,
        float fontSize, bool bold = false, bool italic = false,
        bool centered = true, bool outlined = false, PluginColor outlineColor = default)
        => DrawText(text, x, y, width, height, color, fontSize,
            centered ? TextHAlign.Center : TextHAlign.Left,
            centered ? TextVAlign.Middle : TextVAlign.Top,
            bold, italic, outlined, outlineColor);

    public void DrawText(string text, int x, int y, int width, int height, PluginColor color,
        float fontSize, TextHAlign hAlign, TextVAlign vAlign,
        bool bold = false, bool italic = false, bool outlined = false, PluginColor outlineColor = default)
    {
        if (string.IsNullOrEmpty(text)) return;
        InRegion(() => BitmapHelper.DrawTextAligned(
            _canvas, text, ToSk(color), fontSize, (int)hAlign, (int)vAlign,
            posX: x, posY: y, imageWidth: width, imageHeight: height,
            bold: bold, italic: italic, outlined: outlined, outlineColor: ToSk(outlineColor)));
    }

    public float MeasureText(string text, float fontSize, bool bold = false, bool italic = false) =>
        BitmapHelper.MeasureTextWidth(text, fontSize, bold, italic);

    // ── Symbols ─────────────────────────────────────────────────────────────
    public void DrawSymbol(string symbolId, int x, int y, int width, int height, PluginColor tint) =>
        InRegion(() => BitmapHelper.DrawSymbolGlyph(
            _canvas, symbolId, new SKRect(x, y, x + width, y + height), ToSk(tint)));

    public void DrawSymbol(string symbolId, int x, int y, int width, int height, SymbolStyle style) =>
        InRegion(() => BitmapHelper.DrawSymbolGlyph(
            _canvas, symbolId, new SKRect(x, y, x + width, y + height), ToSk(style.Tint),
            rotation: style.Rotation,
            outlined: style.Outlined, outlineColor: ToSk(style.OutlineColor), outlineWidth: style.OutlineWidth,
            shadow: style.Shadow, shadowColor: ToSk(style.ShadowColor), shadowBlur: style.ShadowBlur,
            shadowOffsetX: style.ShadowOffsetX, shadowOffsetY: style.ShadowOffsetY,
            useGradient: style.UseGradient, gradientStart: ToSk(style.GradientStart),
            gradientEnd: ToSk(style.GradientEnd), gradientAngle: style.GradientAngle));

    // ── Images ──────────────────────────────────────────────────────────────
    public void DrawImage(byte[] imageBytes, int x, int y, int width, int height) =>
        DrawImage(imageBytes, x, y, width, height, 255, PluginColor.White);

    public void DrawImage(byte[] imageBytes, int x, int y, int width, int height, byte opacity, PluginColor tint = default)
    {
        if (imageBytes is not { Length: > 0 }) return;
        InRegion(() =>
        {
            var decoded = BitmapHelper.DecodeCached(imageBytes); // cache-owned; do not dispose
            if (decoded is not { Width: > 0, Height: > 0 }) return;

            var fit = Math.Min(width / (float)decoded.Width, height / (float)decoded.Height);
            var dw = decoded.Width * fit;
            var dh = decoded.Height * fit;
            var left = x + ((width - dw) / 2f);
            var top = y + ((height - dh) / 2f);

            var hasTint = tint.A > 0 && (tint.R != 255 || tint.G != 255 || tint.B != 255 || tint.A != 255);
            if (opacity == 255 && !hasTint)
            {
                _canvas.DrawBitmap(decoded, new SKRect(left, top, left + dw, top + dh));
                return;
            }

            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(opacity), IsAntialias = true };
            if (hasTint)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(ToSk(tint), SKBlendMode.Modulate);
            _canvas.DrawBitmap(decoded, new SKRect(left, top, left + dw, top + dh), paint);
        });
    }

    // ── Transform ───────────────────────────────────────────────────────────
    public void PushTransform() => _transformStack.Push(_transform);

    public void PopTransform()
    {
        if (_transformStack.Count > 0)
            _transform = _transformStack.Pop();
    }

    public void Translate(float dx, float dy) =>
        _transform = SKMatrix.Concat(_transform, SKMatrix.CreateTranslation(dx, dy));

    public void Rotate(float degrees) =>
        _transform = SKMatrix.Concat(_transform, SKMatrix.CreateRotationDegrees(degrees));

    public void Scale(float sx, float sy) =>
        _transform = SKMatrix.Concat(_transform, SKMatrix.CreateScale(sx, sy));

    // ── Paint helpers ───────────────────────────────────────────────────────
    private static SKPaint Fill(PluginColor color) =>
        new() { Color = ToSk(color), Style = SKPaintStyle.Fill, IsAntialias = true };

    private static SKPaint Stroke(PluginColor color, int strokeWidth) =>
        new()
        {
            Color = ToSk(color), Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
        };
}
