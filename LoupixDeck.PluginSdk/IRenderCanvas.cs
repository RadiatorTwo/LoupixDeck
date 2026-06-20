namespace LoupixDeck.PluginSdk;

/// <summary>Horizontal alignment of text within its box.</summary>
public enum TextHAlign
{
    Left,
    Center,
    Right
}

/// <summary>Vertical alignment of text within its box.</summary>
public enum TextVAlign
{
    Top,
    Middle,
    Bottom
}

/// <summary>
/// Optional styling for <see cref="IRenderCanvas.DrawSymbol(string,int,int,int,int,SymbolStyle)"/>:
/// tint, outline, drop shadow, linear gradient fill and rotation. Defaults render a flat tinted
/// glyph. Use an object initializer; only set what you need.
/// </summary>
public readonly record struct SymbolStyle
{
    public SymbolStyle(PluginColor tint)
    {
        Tint = tint;
    }

    /// <summary>Solid fill color (ignored when <see cref="UseGradient"/> is true).</summary>
    public PluginColor Tint { get; init; } = PluginColor.White;

    /// <summary>Rotation in degrees, clockwise.</summary>
    public float Rotation { get; init; }

    public bool Outlined { get; init; }
    public PluginColor OutlineColor { get; init; }
    public float OutlineWidth { get; init; }

    public bool Shadow { get; init; }
    public PluginColor ShadowColor { get; init; }
    public float ShadowBlur { get; init; }
    public int ShadowOffsetX { get; init; }
    public int ShadowOffsetY { get; init; }

    public bool UseGradient { get; init; }
    public PluginColor GradientStart { get; init; }
    public PluginColor GradientEnd { get; init; }
    public float GradientAngle { get; init; }
}

/// <summary>
/// A drawing surface handed to a plugin by the host so it can render onto a device region
/// (a side strip, a strip segment, a touch button …) using the host's own primitives. Text and
/// symbols are rendered by the host, so a plugin's output stays visually consistent with the
/// core (same font, same symbol library), while <see cref="DrawImage(byte[],int,int,int,int)"/>
/// still lets a plugin composite anything it rasterizes itself.
///
/// <para>The canvas is valid <b>only during the synchronous render call that received it</b> —
/// do not cache or use it later. Coordinates are canvas-local: (0,0) is the top-left of this
/// region and <see cref="Width"/>×<see cref="Height"/> is the region size in device pixels.</para>
/// </summary>
public interface IRenderCanvas
{
    /// <summary>Region width in device pixels.</summary>
    int Width { get; }

    /// <summary>Region height in device pixels.</summary>
    int Height { get; }

    /// <summary>Fills the whole region with <paramref name="color"/>.</summary>
    void Clear(PluginColor color);

    // ── Rectangles ──────────────────────────────────────────────────────────
    void FillRectangle(int x, int y, int width, int height, PluginColor color);
    void DrawRectangle(int x, int y, int width, int height, int strokeWidth, PluginColor color);
    void FillRoundedRectangle(int x, int y, int width, int height, int radius, PluginColor color);
    void DrawRoundedRectangle(int x, int y, int width, int height, int radius, int strokeWidth, PluginColor color);

    // ── Circles / ellipses / arcs ───────────────────────────────────────────
    void FillCircle(int centerX, int centerY, int radius, PluginColor color);
    void DrawCircle(int centerX, int centerY, int radius, int strokeWidth, PluginColor color);
    void FillEllipse(int x, int y, int width, int height, PluginColor color);
    void DrawEllipse(int x, int y, int width, int height, int strokeWidth, PluginColor color);

    /// <summary>Strokes an arc of the ellipse bounded by (x,y,width,height). Angles are in
    /// degrees, clockwise, 0° at 3 o'clock. Good for round gauges.</summary>
    void DrawArc(int x, int y, int width, int height, float startAngle, float sweepAngle, int strokeWidth, PluginColor color);

    /// <summary>Fills a pie slice (wedge) of the ellipse bounded by (x,y,width,height).</summary>
    void FillArc(int x, int y, int width, int height, float startAngle, float sweepAngle, PluginColor color);

    // ── Lines ───────────────────────────────────────────────────────────────
    void DrawLine(int x1, int y1, int x2, int y2, int strokeWidth, PluginColor color);

    // ── Text ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Draws word-wrapped text inside the box at (<paramref name="x"/>,<paramref name="y"/>) of
    /// size <paramref name="width"/>×<paramref name="height"/>, in the host's UI font. When
    /// <paramref name="centered"/> is true the text is centered within the box; otherwise it is
    /// top-left aligned. An optional outline is stroked behind the fill.
    /// </summary>
    void DrawText(string text, int x, int y, int width, int height, PluginColor color,
        float fontSize, bool bold = false, bool italic = false,
        bool centered = true, bool outlined = false, PluginColor outlineColor = default);

    /// <summary>
    /// Draws word-wrapped text inside a box with independent horizontal and vertical alignment.
    /// </summary>
    void DrawText(string text, int x, int y, int width, int height, PluginColor color,
        float fontSize, TextHAlign hAlign, TextVAlign vAlign,
        bool bold = false, bool italic = false, bool outlined = false, PluginColor outlineColor = default);

    /// <summary>
    /// Measures the single-line advance width of <paramref name="text"/> in the host's UI font
    /// (no wrapping). Use it to fit/ellipsize text before drawing.
    /// </summary>
    float MeasureText(string text, float fontSize, bool bold = false, bool italic = false);

    // ── Symbols ─────────────────────────────────────────────────────────────
    /// <summary>Draws a symbol from the host's symbol library, tinted, fitted into the given box.
    /// Unknown ids draw a placeholder.</summary>
    void DrawSymbol(string symbolId, int x, int y, int width, int height, PluginColor tint);

    /// <summary>Draws a symbol with full styling (outline/shadow/gradient/rotation).</summary>
    void DrawSymbol(string symbolId, int x, int y, int width, int height, SymbolStyle style);

    // ── Images ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Decodes <paramref name="imageBytes"/> (PNG or any host-decodable format) and blits it
    /// aspect-fit into the given box. The escape hatch for fully custom plugin-rasterized content.
    /// Repeated calls with the <b>same</b> byte array instance reuse a cached decode (so a plugin
    /// holding a static image does not pay the decode cost every frame).
    /// </summary>
    void DrawImage(byte[] imageBytes, int x, int y, int width, int height);

    /// <summary>
    /// As <see cref="DrawImage(byte[],int,int,int,int)"/> with an opacity (0–255) and an optional
    /// tint (multiplied over the image; pass <see cref="PluginColor.White"/> / default for none).
    /// </summary>
    void DrawImage(byte[] imageBytes, int x, int y, int width, int height, byte opacity, PluginColor tint = default);

    // ── Transform ───────────────────────────────────────────────────────────
    /// <summary>Saves the current transform so a later <see cref="PopTransform"/> restores it.</summary>
    void PushTransform();

    /// <summary>Restores the transform saved by the matching <see cref="PushTransform"/>.</summary>
    void PopTransform();

    /// <summary>Translates subsequent drawing by (dx, dy).</summary>
    void Translate(float dx, float dy);

    /// <summary>Rotates subsequent drawing by <paramref name="degrees"/> (clockwise) about the
    /// current origin. Translate to a pivot first to rotate about a point.</summary>
    void Rotate(float degrees);

    /// <summary>Scales subsequent drawing by (sx, sy) about the current origin.</summary>
    void Scale(float sx, float sy);
}
