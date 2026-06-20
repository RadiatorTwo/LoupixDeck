using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using SkiaSharp;

namespace LoupixDeck.Utils;

public static class BitmapHelper
{
    /// <summary>
    /// Resolver used by the renderer to fetch an <see cref="SKBitmap"/> for a
    /// given relative asset path. Wired up at app startup so the static helper
    /// does not need to know about DI. Returns null if unresolved.
    /// </summary>
    public static Func<string, SKBitmap> AssetResolver { get; set; }

    /// <summary>
    /// Cache of "Liberation Sans" typefaces keyed by (weight, slant). Previously a
    /// fresh <see cref="SKTypeface"/> was allocated on every text render, piling up
    /// native objects that the GC finalizer thread later freed concurrently with
    /// active Skia rendering — see <c>docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md</c>
    /// (point 5). Typefaces are long-lived and intentionally never disposed.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (SKFontStyleWeight Weight, SKFontStyleSlant Slant), SKTypeface> TypefaceCache = new();

    private static SKTypeface GetTextTypeface(bool bold, bool italic)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return TypefaceCache.GetOrAdd((weight, slant), key =>
            SKTypeface.FromFamilyName("Liberation Sans", key.Weight, SKFontStyleWidth.Normal, key.Slant));
    }

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

    /// <summary>
    /// Renders an RGB device button as a realistic physical push-button: a dark,
    /// top-lit plastic body with a bevelled rim, and an inner LED ring (with a gap
    /// at the top-right) that glows softly in the assigned colour. The ring keeps a
    /// faint base glow even when the button colour is (near-)black so it stays
    /// visible in the "off" state. Drawn with SkiaSharp (for blur-based glow and
    /// gradient shading) and returned as an Avalonia <see cref="Bitmap"/> so the
    /// existing image bindings stay unchanged.
    /// </summary>
    public static Bitmap RenderSimpleButtonImage(SimpleButton simpleButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(simpleButton);

        // Supersample so the downscaled on-screen image has smooth edges.
        const int ss = 2;
        var w = width * ss;
        var h = height * ss;

        // Unpremultiplied Bgra8888 matches the Avalonia Bitmap construction below
        // (AlphaFormat.Unpremul), avoiding dark fringes on the glow's soft edges.
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        Bitmap result;

        // All Skia drawing happens under the shared render gate so it can never
        // overlap another render/convert on a different thread (see SkiaRenderGate /
        // docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md, measure 1).
        lock (SkiaRenderGate.Sync)
        {
            using var surface = new SKBitmap(info);
            using (var canvas = new SKCanvas(surface))
            {
                canvas.Clear(SKColors.Transparent);

                var cx = w / 2f;
                var cy = h / 2f;

                // Leave room around the body for the seating shadow so its blurred,
                // downward-biased edge stays inside the bitmap instead of being clipped
                // at the canvas edge (obvious on a light background). The margin (9·ss)
                // matches the rotary knob so both seat with the same depth.
                var bodyRadius = Math.Min(w, h) / 2f - 9f * ss;

                // Light/Dark plastic palette so the button body follows the app theme.
                // The bevel rim and glowing LED ring stay the same — the ring colour is
                // device state, not chrome.
                var light = IsLightTheme;

                // 1. Seating shadow: soft dark blob slightly below the body so the
                //    button appears to sit in the panel.
                using (var shadow = new SKPaint
                       {
                           IsAntialias = true,
                           Color = new SKColor(0, 0, 0, (byte)(light ? 110 : 185)),
                           MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f * ss)
                       })
                {
                    canvas.DrawCircle(cx, cy + 3f * ss, bodyRadius, shadow);
                }

                // 2. Button body: a near-flat face. Only a slight vertical gradient so
                //    it reads as a flat disc rather than a domed bulge.
                SKColor[] bodyColors = light
                    ? [new SKColor(0xE2, 0xE2, 0xE2), new SKColor(0xD0, 0xD0, 0xD0)]
                    : [new SKColor(0x33, 0x33, 0x33), new SKColor(0x29, 0x29, 0x29)];
                using (var bodyShader = SKShader.CreateLinearGradient(
                           new SKPoint(cx, cy - bodyRadius),
                           new SKPoint(cx, cy + bodyRadius),
                           bodyColors,
                           [0f, 1f],
                           SKShaderTileMode.Clamp))
                using (var body = new SKPaint { IsAntialias = true, Shader = bodyShader })
                {
                    canvas.DrawCircle(cx, cy, bodyRadius, body);
                }

                // 3. Bevel rim: a thin bright top edge fading to a dark bottom edge.
                //    This alone gives the flat face just enough edge definition.
                var rimWidth = 1.5f * ss;
                using (var rimShader = SKShader.CreateLinearGradient(
                           new SKPoint(cx, cy - bodyRadius),
                           new SKPoint(cx, cy + bodyRadius),
                           [new SKColor(0xFF, 0xFF, 0xFF, 55), new SKColor(0x00, 0x00, 0x00, 110)],
                           [0f, 1f],
                           SKShaderTileMode.Clamp))
                using (var rim = new SKPaint
                       {
                           IsAntialias = true,
                           Style = SKPaintStyle.Stroke,
                           StrokeWidth = rimWidth,
                           Shader = rimShader
                       })
                {
                    canvas.DrawCircle(cx, cy, bodyRadius - rimWidth / 2f, rim);
                }

                // 4. Glowing LED ring with a gap at the top-right.
                var glowColor = ResolveGlowColor(simpleButton.ButtonColor.ToSKColor());
                var coreColor = MixToWhite(glowColor, 0.45f);

                // The body face spans the 11 mm button; size the ring to a 5 mm diameter.
                var ringRadius = bodyRadius * (5f / 11f);
                var oval = new SKRect(cx - ringRadius, cy - ringRadius, cx + ringRadius, cy + ringRadius);

                // Skia angles: 0° = +x, positive = clockwise (y-down). Centre the
                // gap at the top-right (~ -45°) and sweep the remainder.
                const float gapAngle = 50f;
                const float startAngle = -45f + gapAngle / 2f;
                const float sweepAngle = 360f - gapAngle;
                var coreWidth = 2.4f * ss;

                using var ringPath = new SKPath();
                ringPath.AddArc(oval, startAngle, sweepAngle);

                // Wide soft halo.
                using (var halo = new SKPaint
                       {
                           IsAntialias = true,
                           Style = SKPaintStyle.Stroke,
                           StrokeWidth = coreWidth * 3.2f,
                           StrokeCap = SKStrokeCap.Round,
                           Color = glowColor.WithAlpha(110),
                           MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4.5f * ss)
                       })
                {
                    canvas.DrawPath(ringPath, halo);
                }

                // Tighter inner glow.
                using (var innerGlow = new SKPaint
                       {
                           IsAntialias = true,
                           Style = SKPaintStyle.Stroke,
                           StrokeWidth = coreWidth * 1.8f,
                           StrokeCap = SKStrokeCap.Round,
                           Color = glowColor.WithAlpha(180),
                           MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f * ss)
                       })
                {
                    canvas.DrawPath(ringPath, innerGlow);
                }

                // Crisp bright core (the lit filament).
                using (var core = new SKPaint
                       {
                           IsAntialias = true,
                           Style = SKPaintStyle.Stroke,
                           StrokeWidth = coreWidth,
                           StrokeCap = SKStrokeCap.Round,
                           Color = coreColor
                       })
                {
                    canvas.DrawPath(ringPath, core);
                }
            }

            // Copy the pixels into an independent Avalonia bitmap (the constructor
            // copies, so the SKBitmap can be disposed) — same technique as
            // SKBitmapToAvaloniaBitmapConverter.
            var pixels = surface.GetPixels();
            result = new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul,
                pixels,
                new PixelSize(w, h),
                new Vector(96, 96),
                surface.RowBytes);
            GC.KeepAlive(surface);
        }

        return result;
    }

    /// <summary>
    /// Resolves the glow colour for the LED ring. Returns the assigned colour as-is
    /// whenever it carries any visible hue; only a fully transparent or (near-)black
    /// "off" colour falls back to a faint neutral grey so the ring stays softly
    /// visible. Uses the brightest channel (not perceived luminance) so saturated but
    /// perceptually dark colours like pure blue keep their own colour.
    /// </summary>
    private static SKColor ResolveGlowColor(SKColor c)
    {
        var maxChannel = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        return c.Alpha < 8 || maxChannel < 24 ? new SKColor(0x3A, 0x3A, 0x3A) : c;
    }

    /// <summary>Blends <paramref name="c"/> toward white by factor <paramref name="t"/> (0..1).</summary>
    private static SKColor MixToWhite(SKColor c, float t)
    {
        byte Mix(byte v) => (byte)(v + (255 - v) * t);
        return new SKColor(Mix(c.Red), Mix(c.Green), Mix(c.Blue), c.Alpha);
    }

    private static Bitmap _rotaryKnobImage;
    private static bool _rotaryKnobImageLight;

    /// <summary>
    /// True when the application is currently showing the Light theme variant. The
    /// simulated-device plastic is rendered lighter in Light mode so the knob and LED
    /// buttons follow the theme like the rest of the chrome.
    /// </summary>
    private static bool IsLightTheme =>
        Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;

    /// <summary>
    /// A shared, lazily-rendered image of the rotary dial. The knob has no per-button
    /// state, so every dial in every layout binds to this single cached bitmap (via a
    /// VM property). Re-rendered when the theme variant changes. Rendered at 90 px to
    /// match the 15 mm base at 6 px/mm.
    /// </summary>
    public static Bitmap RotaryKnobImage
    {
        get
        {
            var light = IsLightTheme;
            if (_rotaryKnobImage == null || _rotaryKnobImageLight != light)
            {
                _rotaryKnobImage = RenderRotaryKnobImage(90, 90);
                _rotaryKnobImageLight = light;
            }

            return _rotaryKnobImage;
        }
    }

    /// <summary>
    /// Renders a rotary dial as a realistic stepped knob built from three concentric
    /// tiers matching the real device — a 15 mm base, a 12 mm mid-step and an 11 mm
    /// top cap. Each tier is top-lit with its own bevelled rim, and the steps between
    /// tiers carry a soft contact shadow so the knob reads as a physical stacked
    /// cylinder. Drawn with SkiaSharp (for blur-based shadows and gradient shading)
    /// and returned as an Avalonia <see cref="Bitmap"/> so the existing image
    /// bindings stay unchanged.
    /// </summary>
    public static Bitmap RenderRotaryKnobImage(int width, int height)
    {
        // Supersample so the downscaled on-screen image has smooth edges.
        const int ss = 2;
        var w = width * ss;
        var h = height * ss;

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        Bitmap result;

        // All Skia drawing happens under the shared render gate so it can never
        // overlap another render/convert on a different thread (see SkiaRenderGate /
        // docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md, measure 1).
        lock (SkiaRenderGate.Sync)
        {
            using var surface = new SKBitmap(info);
            using (var canvas = new SKCanvas(surface))
            {
                canvas.Clear(SKColors.Transparent);

                var cx = w / 2f;
                var cy = h / 2f;

                // Leave room around the base for the seating shadow so its blurred,
                // downward-biased edge stays inside the bitmap — otherwise it gets
                // clipped at the canvas edge, which is glaringly obvious on a light
                // background. The margin (9·ss) is sized so the full dense shadow
                // (offset + radius + blur) fits within the canvas. The two upper tiers
                // are sized from the real diameters (12/15 and 11/15 of base).
                var baseRadius = Math.Min(w, h) / 2f - 9f * ss;
                var midRadius = baseRadius * (12f / 15f);
                var topRadius = baseRadius * (11f / 15f);

                // Light/Dark plastic palette so the knob follows the app theme. The
                // top-lit shading, rims and contact shadows stay the same; only the
                // base plastic tone flips.
                var light = IsLightTheme;

                // 1. Seating shadow: soft dark blob slightly below the base so the
                //    knob appears to sit in the panel.
                using (var shadow = new SKPaint
                       {
                           IsAntialias = true,
                           Color = new SKColor(0, 0, 0, (byte)(light ? 110 : 185)),
                           MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f * ss)
                       })
                {
                    canvas.DrawCircle(cx, cy + 3f * ss, baseRadius, shadow);
                }

                // 2. Base tier (15 mm).
                DrawKnobTier(canvas, cx, cy, baseRadius,
                    light ? new SKColor(0xE6, 0xE6, 0xE6) : new SKColor(0x2B, 0x2B, 0x2B),
                    light ? new SKColor(0xCC, 0xCC, 0xCC) : new SKColor(0x14, 0x14, 0x14), ss);

                // 3. Contact shadow cast by the mid tier onto the exposed base ring.
                DrawStepShadow(canvas, cx, cy, midRadius, ss);

                // 4. Mid tier (12 mm) — a touch lighter so the step reads.
                DrawKnobTier(canvas, cx, cy, midRadius,
                    light ? new SKColor(0xF0, 0xF0, 0xF0) : new SKColor(0x36, 0x36, 0x36),
                    light ? new SKColor(0xD6, 0xD6, 0xD6) : new SKColor(0x1B, 0x1B, 0x1B), ss);

                // 5. Grip ridges: 12 radial tactile grooves only on the rotating
                //    mid-tier side band (between the top cap and the mid edge). The
                //    base flange stays smooth.
                DrawGripRidges(canvas, cx, cy, topRadius, midRadius, 12, ss);

                // 6. Contact shadow cast by the top cap onto the exposed mid ring.
                DrawStepShadow(canvas, cx, cy, topRadius, ss);

                // 7. Top cap (11 mm) — the smooth surface the user touches.
                DrawKnobTop(canvas, cx, cy, topRadius, ss, light);
            }

            // Copy the pixels into an independent Avalonia bitmap (the constructor
            // copies, so the SKBitmap can be disposed) — same technique as
            // RenderSimpleButtonImage.
            var pixels = surface.GetPixels();
            result = new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul,
                pixels,
                new PixelSize(w, h),
                new Vector(96, 96),
                surface.RowBytes);
            GC.KeepAlive(surface);
        }

        return result;
    }

    /// <summary>
    /// Draws one knob tier: a top-lit dark disc with a bevelled rim (a bright top
    /// edge fading to a dark bottom edge) that gives the flat face edge definition.
    /// </summary>
    private static void DrawKnobTier(SKCanvas canvas, float cx, float cy, float radius,
        SKColor top, SKColor bottom, float scale)
    {
        using (var faceShader = SKShader.CreateLinearGradient(
                   new SKPoint(cx, cy - radius),
                   new SKPoint(cx, cy + radius),
                   [top, bottom],
                   [0f, 1f],
                   SKShaderTileMode.Clamp))
        using (var face = new SKPaint { IsAntialias = true, Shader = faceShader })
        {
            canvas.DrawCircle(cx, cy, radius, face);
        }

        var rimWidth = 1.6f * scale;
        using (var rimShader = SKShader.CreateLinearGradient(
                   new SKPoint(cx, cy - radius),
                   new SKPoint(cx, cy + radius),
                   [new SKColor(0xFF, 0xFF, 0xFF, 70), new SKColor(0x00, 0x00, 0x00, 130)],
                   [0f, 1f],
                   SKShaderTileMode.Clamp))
        using (var rim = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = rimWidth,
                   Shader = rimShader
               })
        {
            canvas.DrawCircle(cx, cy, radius - rimWidth / 2f, rim);
        }
    }

    /// <summary>
    /// Draws the soft contact shadow an upper tier casts onto the tier below, as a
    /// blurred dark ring hugging the upper tier's edge (biased slightly downward to
    /// match the top lighting).
    /// </summary>
    private static void DrawStepShadow(SKCanvas canvas, float cx, float cy, float radius, float scale)
    {
        using var p = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f * scale,
            Color = new SKColor(0, 0, 0, 150),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2f * scale)
        };
        canvas.DrawCircle(cx, cy + 0.5f * scale, radius + 1.5f * scale, p);
    }

    /// <summary>
    /// Draws the smooth top cap: a top-lit radial face, a bevelled rim and a soft
    /// specular highlight near the top.
    /// </summary>
    private static void DrawKnobTop(SKCanvas canvas, float cx, float cy, float radius, float scale,
        bool light = false)
    {
        // Radial face whose light centre is offset upward so it reads as top-lit.
        SKColor[] faceColors = light
            ? [new SKColor(0xF4, 0xF4, 0xF4), new SKColor(0xD2, 0xD2, 0xD2)]
            : [new SKColor(0x4A, 0x4A, 0x4A), new SKColor(0x1E, 0x1E, 0x1E)];
        using (var faceShader = SKShader.CreateRadialGradient(
                   new SKPoint(cx, cy - radius * 0.35f),
                   radius * 1.3f,
                   faceColors,
                   [0f, 1f],
                   SKShaderTileMode.Clamp))
        using (var face = new SKPaint { IsAntialias = true, Shader = faceShader })
        {
            canvas.DrawCircle(cx, cy, radius, face);
        }

        var rimWidth = 1.6f * scale;
        using (var rimShader = SKShader.CreateLinearGradient(
                   new SKPoint(cx, cy - radius),
                   new SKPoint(cx, cy + radius),
                   [new SKColor(0xFF, 0xFF, 0xFF, 95), new SKColor(0x00, 0x00, 0x00, 140)],
                   [0f, 1f],
                   SKShaderTileMode.Clamp))
        using (var rim = new SKPaint
               {
                   IsAntialias = true,
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = rimWidth,
                   Shader = rimShader
               })
        {
            canvas.DrawCircle(cx, cy, radius - rimWidth / 2f, rim);
        }

        // Soft specular highlight near the top edge.
        using (var highlight = new SKPaint
               {
                   IsAntialias = true,
                   Color = new SKColor(0xFF, 0xFF, 0xFF, 55),
                   MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f * scale)
               })
        {
            canvas.DrawOval(
                new SKRect(cx - radius * 0.5f, cy - radius * 0.72f,
                    cx + radius * 0.5f, cy - radius * 0.18f),
                highlight);
        }
    }

    /// <summary>
    /// Draws <paramref name="count"/> evenly spaced radial grip grooves across the
    /// side band (the <paramref name="innerR"/>..<paramref name="outerR"/> annulus).
    /// Each groove is a dark radial line paired with a thin highlight on its
    /// clockwise side so the raised ridges between grooves read as tactile bumps.
    /// </summary>
    private static void DrawGripRidges(SKCanvas canvas, float cx, float cy,
        float innerR, float outerR, int count, float scale)
    {
        // Angular offset (radians) of the catch-light relative to its groove.
        const float highlightOffset = 0.05f;

        using var groove = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.3f * scale,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(0x00, 0x00, 0x00, 140)
        };
        using var highlight = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.9f * scale,
            StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 60)
        };

        for (var i = 0; i < count; i++)
        {
            var angle = (float)(i * 2 * Math.PI / count);
            DrawRadialLine(canvas, cx, cy, innerR, outerR, angle + highlightOffset, highlight);
            DrawRadialLine(canvas, cx, cy, innerR, outerR, angle, groove);
        }
    }

    /// <summary>Strokes a radial line from <paramref name="innerR"/> to <paramref name="outerR"/> at the given angle.</summary>
    private static void DrawRadialLine(SKCanvas canvas, float cx, float cy,
        float innerR, float outerR, float angle, SKPaint paint)
    {
        var dx = (float)Math.Cos(angle);
        var dy = (float)Math.Sin(angle);
        canvas.DrawLine(
            cx + dx * innerR, cy + dy * innerR,
            cx + dx * outerR, cy + dy * outerR,
            paint);
    }

    /// <summary>
    /// Returns the slot's baked bitmap at <paramref name="width"/>×<paramref name="height"/>
    /// (480×270 for the main panel, 60×270 for a side display), computing and caching it
    /// on first use. The original image is resolved from the asset folder via
    /// <see cref="AssetResolver"/>, scaled/positioned with the slot's parameters and
    /// optionally horizontally mirrored. Returns null when the slot has no image or the
    /// asset is missing.
    /// </summary>
    public static SKBitmap GetOrBakeSlot(WallpaperSlot slot, int width, int height)
    {
        if (slot == null || !slot.HasImage) return null;
        if (slot.Baked != null) return slot.Baked;

        var original = AssetResolver?.Invoke(slot.AssetPath);
        if (original == null) return null;

        SKBitmap baked;
        lock (SkiaRenderGate.Sync)
        {
            baked = ScaleAndPositionBitmap(
                original, width, height,
                slot.Scaling, slot.PositionX, slot.PositionY, slot.ScalingOption);

            if (slot.Mirror)
            {
                var flipped = FlipHorizontal(baked);
                baked.Dispose();
                baked = flipped;
            }
        }

        slot.Baked = baked;
        return baked;
    }

    /// <summary>Returns a horizontally mirrored copy of <paramref name="source"/>.</summary>
    private static SKBitmap FlipHorizontal(SKBitmap source)
    {
        var dst = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.Translate(source.Width, 0);
        canvas.Scale(-1, 1);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static bool HasAnyWallpaper(TouchButtonPage page) =>
        page != null &&
        ((page.MainWallpaper?.HasImage ?? false) ||
         (page.LeftWallpaper?.HasImage ?? false) ||
         (page.RightWallpaper?.HasImage ?? false));

    /// <summary>
    /// Picks the page whose wallpapers should be drawn for the current view: the current
    /// page when it has <b>any</b> of its three wallpapers set, otherwise page 0 as a
    /// fallback. Once a page defines a wallpaper it is self-contained — its empty slots
    /// stay empty rather than borrowing from page 0 (so a page that only sets a main
    /// wallpaper does not inherit another page's side wallpapers).
    /// </summary>
    private static TouchButtonPage ResolveWallpaperSourcePage(LoupedeckConfig config)
    {
        var current = config?.CurrentTouchButtonPage;
        if (HasAnyWallpaper(current)) return current;
        if (config?.TouchButtonPages is { Count: > 0 }) return config.TouchButtonPages[0];
        return current;
    }

    /// <summary>
    /// Resolves the baked main wallpaper (and its opacity dim) to draw for the current
    /// view. Returns (null, 0) when the source page has no main wallpaper. Shared by the
    /// centre grid and the Razer side strips so they stay in sync.
    /// </summary>
    private static (SKBitmap wallpaper, double opacity) ResolveWallpaper(LoupedeckConfig config)
    {
        var page = ResolveWallpaperSourcePage(config);
        if (page == null) return (null, 0);

        var baked = GetOrBakeSlot(page.MainWallpaper, PanelWidth, PanelHeight);
        return baked != null ? (baked, page.MainWallpaper.Opacity) : (null, 0);
    }

    /// <summary>
    /// Resolves the optional side-display wallpaper for <paramref name="side"/> on the
    /// source page, baked to the strip size. Returns (null, 0) when that side has no
    /// dedicated wallpaper — callers then fall back to the main wallpaper's panel region.
    /// </summary>
    private static (SKBitmap wallpaper, double opacity) ResolveSideWallpaper(
        LoupedeckConfig config, RotarySide side, int width, int height)
    {
        var page = ResolveWallpaperSourcePage(config);
        if (page == null) return (null, 0);

        var slot = side == RotarySide.Left ? page.LeftWallpaper : page.RightWallpaper;
        var baked = GetOrBakeSlot(slot, width, height);
        return baked != null ? (baked, slot.Opacity) : (null, 0);
    }

    /// <summary>
    /// Renders the content of a TouchButton (background, image, text) into an Avalonia bitmap.
    /// </summary>
    public static SKBitmap RenderTouchButtonContent(
        TouchButton touchButton,
        LoupedeckConfig config,
        int width,
        int height,
        int gridColumns = 0,
        int wallpaperXOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        // Determine which wallpaper to use: current page's or fallback to first page's.
        // ResolveWallpaper bakes the 480×270 bitmap from the page's asset on demand.
        var (wallpaperToUse, opacityToUse) = ResolveWallpaper(config);

        // All SkiaSharp drawing happens under the shared render gate so it can never
        // overlap another render/convert running on a different thread (see
        // SkiaRenderGate / docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md, measure 1). Only
        // the finished-bitmap publish + return is left outside the lock.
        SKBitmap bitmap;
        lock (SkiaRenderGate.Sync)
        {
            // Create SKBitmap for rendering
            bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            if (wallpaperToUse != null && gridColumns > 0)
            {
                // Determine the position of the button in the grid
                var col = touchButton.Index % gridColumns;
                var row = touchButton.Index / gridColumns;

                // Calculate the section from the wallpaper. wallpaperXOffset shifts the
                // sampled region to the grid's true panel position so the wallpaper stays
                // continuous with the side strips across the bezel (Razer: +60).
                var srcRect = new SKRect(
                    wallpaperXOffset + col * width,
                    row * height,
                    wallpaperXOffset + (col + 1) * width,
                    (row + 1) * height
                );
                var destRect = new SKRect(0, 0, width, height);

                // Draw Wallpaper Cutout
                canvas.DrawBitmap(wallpaperToUse, srcRect, destRect);

                // Semi-transparent background
                using var paint = new SKPaint();

                paint.Color = new SKColor(0, 0, 0, (byte)(255 * opacityToUse));

                canvas.DrawRect(destRect, paint);
            }
            else
            {
                // Draw Monochrome Background
                canvas.Clear(touchButton.BackColor.ToSKColor());
            }

            DrawLayers(canvas, touchButton.Layers, width, height);
        }

        // Publish the finished bitmap (fires OnPropertyChanged for the UI binding);
        // kept outside the gate so UI-marshalled work never runs while the lock is held.
        touchButton.RenderedImage = bitmap;

        return bitmap;
    }

    /// <summary>
    /// Renders the touch-button settings dialog preview: a <paramref name="canvasSize"/>
    /// square with a centered <paramref name="frameSize"/>-pixel frame representing the
    /// real 90×90 device area. Layers may extend beyond the frame so the user can drag
    /// images past the button edge — frame clipping happens only on the device-side
    /// render produced by <see cref="RenderTouchButtonContent"/>.
    /// </summary>
    /// <summary>Largest editor-frame extent (canvas px). The frame's longer side maps
    /// to this; the shorter side scales proportionally. A square 90×90 button yields a
    /// 300×300 frame (scale 3.333), matching the original fixed layout.</summary>
    public const int EditorFrameExtent = 300;

    /// <summary>
    /// Computes the editor frame geometry for a device surface of
    /// <paramref name="deviceWidth"/>×<paramref name="deviceHeight"/>: a uniform
    /// canvas-px-per-device-px scale (so aspect is preserved) and the resulting
    /// frame width/height. Used by both the renderer and the View's overlay math.
    /// </summary>
    public static (float Scale, float FrameWidth, float FrameHeight) ComputeEditorFrame(int deviceWidth, int deviceHeight)
    {
        var scale = EditorFrameExtent / (float)Math.Max(deviceWidth, deviceHeight);
        return (scale, deviceWidth * scale, deviceHeight * scale);
    }

    public static SKBitmap RenderEditorCanvas(
        TouchButton touchButton,
        LoupedeckConfig config,
        int canvasSize = 600,
        int deviceWidth = 90,
        int deviceHeight = 90,
        bool drawGrid = false,
        int gridStepDevice = 10,
        int segmentCount = 0)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        var (scale, frameW, frameH) = ComputeEditorFrame(deviceWidth, deviceHeight);

        var bmp = new SKBitmap(canvasSize, canvasSize);
        using var canvas = new SKCanvas(bmp);

        // Transparent outside the button frame — the editor window paints the
        // theme-aware canvas background (and plate shadow) behind this bitmap.
        canvas.Clear(SKColors.Transparent);

        var frameOffsetX = (canvasSize - frameW) / 2f;
        var frameOffsetY = (canvasSize - frameH) / 2f;
        var frameRect = new SKRect(frameOffsetX, frameOffsetY,
            frameOffsetX + frameW, frameOffsetY + frameH);

        // Fill the frame with the button's background color (the device-pixel area).
        using (var bgPaint = new SKPaint { Color = touchButton.BackColor.ToSKColor() })
        {
            canvas.DrawRect(frameRect, bgPaint);
        }

        // Switch to device-pixel space inside the frame so layer positions/scale match
        // exactly what the device render produces.
        canvas.Save();
        canvas.Translate(frameOffsetX, frameOffsetY);
        canvas.Scale(scale);

        if (touchButton.Layers != null)
        {
            foreach (var layer in touchButton.Layers)
            {
                if (layer == null || !layer.Visible) continue;

                switch (layer)
                {
                    case ImageLayer image:
                        DrawImageLayerExtended(canvas, image, deviceWidth, deviceHeight);
                        break;
                    case TextLayer text:
                        DrawTextLayer(canvas, text, deviceWidth, deviceHeight);
                        break;
                    case SymbolLayer symbol:
                        DrawSymbolLayer(canvas, symbol, deviceWidth, deviceHeight);
                        break;
                    case PluginLayer plugin:
                        DrawPluginLayerExtended(canvas, plugin, deviceWidth, deviceHeight);
                        break;
                }
            }
        }

        canvas.Restore();

        // Optional alignment grid, drawn on top of the layers but kept subtle so it
        // never competes with the content. Lines are spaced in device pixels and
        // mapped to canvas space so they line up with the snap lattice.
        if (drawGrid && gridStepDevice > 0)
        {
            using var gridPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 38),
                StrokeWidth = 1,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke
            };

            for (var dx = 0; dx <= deviceWidth; dx += gridStepDevice)
            {
                var x = frameOffsetX + dx * scale;
                canvas.DrawLine(x, frameOffsetY, x, frameOffsetY + frameH, gridPaint);
            }

            for (var dy = 0; dy <= deviceHeight; dy += gridStepDevice)
            {
                var y = frameOffsetY + dy * scale;
                canvas.DrawLine(frameOffsetX, y, frameOffsetX + frameW, y, gridPaint);
            }

            // Segment dividers (free-draw side strips): stronger horizontal lines marking
            // the boundaries between the tap zones, so the three segments read clearly.
            // Gated on the grid toggle, per the editor's "show only with grid" behaviour.
            if (segmentCount > 1)
            {
                using var segPaint = new SKPaint
                {
                    Color = new SKColor(91, 155, 213, 220),
                    StrokeWidth = 2,
                    IsAntialias = false,
                    Style = SKPaintStyle.Stroke
                };

                for (var k = 1; k < segmentCount; k++)
                {
                    var dy = deviceHeight * k / (float)segmentCount;
                    var y = frameOffsetY + dy * scale;
                    canvas.DrawLine(frameOffsetX, y, frameOffsetX + frameW, y, segPaint);
                }
            }
        }

        return bmp;
    }

    /// <summary>
    /// Returns the on-canvas (editor-preview) rectangle that a layer currently occupies.
    /// Used by the View to position the selection overlay. Returns null if the layer has
    /// no resolvable geometry (e.g. image with missing asset).
    /// </summary>
    public static SKRect? GetLayerEditorBounds(
        LayerBase layer,
        int canvasSize = 600,
        int deviceWidth = 90,
        int deviceHeight = 90)
    {
        if (layer == null || !layer.Visible) return null;

        var deviceRect = GetLayerDeviceRect(layer, deviceWidth, deviceHeight);
        if (deviceRect == null) return null;

        var (scale, frameW, frameH) = ComputeEditorFrame(deviceWidth, deviceHeight);
        var frameOffsetX = (canvasSize - frameW) / 2f;
        var frameOffsetY = (canvasSize - frameH) / 2f;
        var dr = deviceRect.Value;
        return new SKRect(
            frameOffsetX + dr.Left * scale,
            frameOffsetY + dr.Top * scale,
            frameOffsetX + dr.Right * scale,
            frameOffsetY + dr.Bottom * scale);
    }

    /// <summary>
    /// Returns the bounding rectangle of a layer in 90×90 device-pixel space.
    /// Mirrors the geometry math used in <see cref="DrawImageLayerExtended"/> /
    /// <see cref="DrawSymbolLayer"/> so the selection overlay always matches
    /// the rendered output.
    /// </summary>
    private static SKRect? GetLayerDeviceRect(LayerBase layer, int deviceW, int deviceH)
    {
        switch (layer)
        {
            case ImageLayer image:
            {
                var bmp = image.CachedImage;
                if (bmp == null && !string.IsNullOrEmpty(image.AssetRelativePath) && AssetResolver != null)
                {
                    bmp = AssetResolver(image.AssetRelativePath);
                    image.CachedImage = bmp;
                }
                if (bmp == null) return null;

                float srcW, srcH;
                if (!image.SourceRect.IsEmpty &&
                    image.SourceRect.Width > 0 && image.SourceRect.Height > 0)
                {
                    srcW = image.SourceRect.Width;
                    srcH = image.SourceRect.Height;
                }
                else
                {
                    srcW = bmp.Width;
                    srcH = bmp.Height;
                }

                var fit = Math.Min(deviceW / srcW, deviceH / srcH);
                var scaleX = (float)Math.Max(0.01, image.EffectiveScaleX);
                var scaleY = (float)Math.Max(0.01, image.EffectiveScaleY);
                var dstW = srcW * fit * scaleX;
                var dstH = srcH * fit * scaleY;
                var drawX = (deviceW - dstW) / 2f + image.PositionX;
                var drawY = (deviceH - dstH) / 2f + image.PositionY;
                return new SKRect(drawX, drawY, drawX + dstW, drawY + dstH);
            }
            case SymbolLayer symbol:
            {
                var baseSize = Math.Min(deviceW, deviceH);
                var dstW = baseSize * (float)Math.Max(0.01, symbol.EffectiveScaleX);
                var dstH = baseSize * (float)Math.Max(0.01, symbol.EffectiveScaleY);
                var cx = deviceW / 2f + symbol.PositionX;
                var cy = deviceH / 2f + symbol.PositionY;
                return new SKRect(cx - dstW / 2f, cy - dstH / 2f, cx + dstW / 2f, cy + dstH / 2f);
            }
            case TextLayer text:
                return MeasureTextDeviceRect(text, deviceW, deviceH);
            case PluginLayer plugin:
            {
                var bmp = plugin.RenderedBitmap;
                if (bmp is not { Width: > 0, Height: > 0 }) return null;

                var fit = Math.Min(deviceW / (float)bmp.Width, deviceH / (float)bmp.Height);
                var scaleX = (float)Math.Max(0.01, plugin.EffectiveScaleX);
                var scaleY = (float)Math.Max(0.01, plugin.EffectiveScaleY);
                var dstW = bmp.Width * fit * scaleX;
                var dstH = bmp.Height * fit * scaleY;
                var drawX = (deviceW - dstW) / 2f + plugin.PositionX;
                var drawY = (deviceH - dstH) / 2f + plugin.PositionY;
                return new SKRect(drawX, drawY, drawX + dstW, drawY + dstH);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the bounding rectangle (in device-pixel space) that the given text
    /// layer occupies when rendered by <see cref="DrawTextAt"/>. Mirrors the same
    /// font metrics + wrap logic so the editor selection overlay tracks the text.
    /// </summary>
    /// <summary>
    /// Returns the text-layout box rectangle in device-pixel space. This is the
    /// area the renderer wraps text into; the selection overlay tracks it so the
    /// user can drag the corners/edges to enlarge or shrink the wrap area.
    /// </summary>
    private static SKRect MeasureTextDeviceRect(TextLayer layer, int deviceW, int deviceH)
    {
        var (boxLeft, boxTop) = TextBoxOrigin(layer, deviceW, deviceH);
        return new SKRect(boxLeft, boxTop,
            boxLeft + layer.EffectiveBoxWidth,
            boxTop + layer.EffectiveBoxHeight);
    }

    /// <summary>
    /// Editor-canvas variant of <see cref="DrawImageLayer"/> that does NOT pre-render to
    /// a clipped 90×90 surface — it lets the image overflow the device area so the user
    /// can see what they're dragging past the frame.
    /// </summary>
    private static void DrawImageLayerExtended(SKCanvas canvas, ImageLayer layer, int deviceW, int deviceH)
    {
        var bmp = layer.CachedImage;
        if (bmp == null && !string.IsNullOrEmpty(layer.AssetRelativePath) && AssetResolver != null)
        {
            bmp = AssetResolver(layer.AssetRelativePath);
            layer.CachedImage = bmp;
        }
        if (bmp == null) return;

        SKRect srcRect;
        if (!layer.SourceRect.IsEmpty &&
            layer.SourceRect.Width > 0 && layer.SourceRect.Height > 0)
        {
            srcRect = layer.SourceRect.ToSKRect();
        }
        else
        {
            srcRect = new SKRect(0, 0, bmp.Width, bmp.Height);
        }

        var fit = Math.Min(deviceW / srcRect.Width, deviceH / srcRect.Height);
        var scaleX = (float)Math.Max(0.01, layer.EffectiveScaleX);
        var scaleY = (float)Math.Max(0.01, layer.EffectiveScaleY);
        var dstW = srcRect.Width * fit * scaleX;
        var dstH = srcRect.Height * fit * scaleY;
        var drawX = (deviceW - dstW) / 2f + layer.PositionX;
        var drawY = (deviceH - dstH) / 2f + layer.PositionY;

        canvas.DrawBitmap(bmp, srcRect, new SKRect(drawX, drawY, drawX + dstW, drawY + dstH));
    }

    /// <summary>
    /// Iterates the layer collection in order (later entries paint on top) and
    /// dispatches to the appropriate per-layer renderer.
    /// </summary>
    private static void DrawLayers(SKCanvas canvas,
        System.Collections.ObjectModel.ObservableCollection<LayerBase> layers,
        int width, int height)
    {
        if (layers == null) return;

        foreach (var layer in layers)
        {
            if (layer == null || !layer.Visible) continue;

            switch (layer)
            {
                case ImageLayer image:
                    DrawImageLayer(canvas, image, width, height);
                    break;
                case TextLayer text:
                    DrawTextLayer(canvas, text, width, height);
                    break;
                case SymbolLayer symbol:
                    DrawSymbolLayer(canvas, symbol, width, height);
                    break;
                case PluginLayer plugin:
                    DrawPluginLayer(canvas, plugin, width, height);
                    break;
            }
        }
    }

    private static void DrawImageLayer(SKCanvas canvas, ImageLayer layer, int width, int height)
    {
        var bmp = layer.CachedImage;
        if (bmp == null && !string.IsNullOrEmpty(layer.AssetRelativePath) && AssetResolver != null)
        {
            bmp = AssetResolver(layer.AssetRelativePath);
            layer.CachedImage = bmp;
        }
        if (bmp == null) return;

        // Draw directly onto the target canvas so layer alpha composites correctly.
        // The previous approach materialised an intermediate SKBitmap with the source's
        // AlphaType — for opaque sources (e.g. JPEG) the "transparent" surrounding area
        // stayed fully opaque black and overwrote any layers drawn beneath this one.
        var srcRect = (!layer.SourceRect.IsEmpty &&
                       layer.SourceRect.Width > 0 && layer.SourceRect.Height > 0)
            ? layer.SourceRect.ToSKRect()
            : new SKRect(0, 0, bmp.Width, bmp.Height);

        var fit = Math.Min(width / srcRect.Width, height / srcRect.Height);
        var scaleX = (float)Math.Max(0.01, layer.EffectiveScaleX);
        var scaleY = (float)Math.Max(0.01, layer.EffectiveScaleY);
        var dstW = srcRect.Width * fit * scaleX;
        var dstH = srcRect.Height * fit * scaleY;
        var drawX = (width - dstW) / 2f + layer.PositionX;
        var drawY = (height - dstH) / 2f + layer.PositionY;

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, width, height));
        canvas.DrawBitmap(bmp, srcRect, new SKRect(drawX, drawY, drawX + dstW, drawY + dstH));
        canvas.Restore();
    }

    /// <summary>
    /// Device renderer for a <see cref="PluginLayer"/>: blits the plugin-pushed
    /// <see cref="PluginLayer.RenderedBitmap"/> with the same aspect-fit + scale + position
    /// math as <see cref="DrawImageLayer"/> (clipped to the button). Draws nothing until the
    /// plugin pushes its first bitmap.
    /// </summary>
    private static void DrawPluginLayer(SKCanvas canvas, PluginLayer layer, int width, int height)
    {
        var bmp = layer.RenderedBitmap;
        if (bmp is { Width: > 0, Height: > 0 })
        {
            var fit = Math.Min(width / (float)bmp.Width, height / (float)bmp.Height);
            var scaleX = (float)Math.Max(0.01, layer.EffectiveScaleX);
            var scaleY = (float)Math.Max(0.01, layer.EffectiveScaleY);
            var dstW = bmp.Width * fit * scaleX;
            var dstH = bmp.Height * fit * scaleY;
            var drawX = (width - dstW) / 2f + layer.PositionX;
            var drawY = (height - dstH) / 2f + layer.PositionY;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, width, height));
            ApplyRotation(canvas, layer.Rotation, drawX + dstW / 2f, drawY + dstH / 2f);
            canvas.DrawBitmap(bmp, new SKRect(drawX, drawY, drawX + dstW, drawY + dstH));
            canvas.Restore();
        }
    }

    /// <summary>Rotates the canvas by <paramref name="degrees"/> around (cx, cy) when non-zero.</summary>
    private static void ApplyRotation(SKCanvas canvas, double degrees, float cx, float cy)
    {
        if (Math.Abs(degrees) < 0.01) return;
        canvas.RotateDegrees((float)degrees, cx, cy);
    }

    /// <summary>
    /// Editor-canvas variant of <see cref="DrawPluginLayer"/> that does NOT clip to the
    /// device area (so the user sees content dragged past the frame), mirroring
    /// <see cref="DrawImageLayerExtended"/>.
    /// </summary>
    private static void DrawPluginLayerExtended(SKCanvas canvas, PluginLayer layer, int deviceW, int deviceH)
    {
        var bmp = layer.RenderedBitmap;
        if (bmp is { Width: > 0, Height: > 0 })
        {
            var fit = Math.Min(deviceW / (float)bmp.Width, deviceH / (float)bmp.Height);
            var scaleX = (float)Math.Max(0.01, layer.EffectiveScaleX);
            var scaleY = (float)Math.Max(0.01, layer.EffectiveScaleY);
            var dstW = bmp.Width * fit * scaleX;
            var dstH = bmp.Height * fit * scaleY;
            var drawX = (deviceW - dstW) / 2f + layer.PositionX;
            var drawY = (deviceH - dstH) / 2f + layer.PositionY;

            canvas.Save();
            ApplyRotation(canvas, layer.Rotation, drawX + dstW / 2f, drawY + dstH / 2f);
            canvas.DrawBitmap(bmp, new SKRect(drawX, drawY, drawX + dstW, drawY + dstH));
            canvas.Restore();
        }
    }

    private static void DrawTextLayer(SKCanvas canvas, TextLayer layer, int width, int height)
    {
        if (string.IsNullOrEmpty(layer.Text)) return;

        var boxW = layer.EffectiveBoxWidth;
        var boxH = layer.EffectiveBoxHeight;
        var (boxLeft, boxTop) = TextBoxOrigin(layer, width, height);

        var saved = canvas.Save();
        canvas.Translate(boxLeft, boxTop);

        DrawTextAt(
            canvas,
            layer.Text,
            layer.TextColor.ToSKColor(),
            layer.TextSize,
            layer.Centered,
            posX: 0,
            posY: 0,
            imageWidth: boxW,
            imageHeight: boxH,
            layer.Bold,
            layer.Italic,
            layer.Outlined,
            layer.OutlineColor.ToSKColor());

        canvas.RestoreToCount(saved);
    }

    private static (float Left, float Top) TextBoxOrigin(TextLayer layer, int deviceW, int deviceH)
    {
        var boxW = layer.EffectiveBoxWidth;
        var boxH = layer.EffectiveBoxHeight;
        if (layer.Centered)
        {
            return ((deviceW - boxW) / 2f + layer.PositionX,
                    (deviceH - boxH) / 2f + layer.PositionY);
        }
        return (layer.PositionX, layer.PositionY);
    }

    /// <summary>
    /// Renders a <see cref="SymbolLayer"/> as a glyph from the bundled Material
    /// Design Icons font. The glyph is converted to an <see cref="SKPath"/> and
    /// transformed into device-pixel space (so outline width and shadow blur stay
    /// consistent regardless of symbol scale), then drawn shadow → outline → fill.
    /// The glyph's tight bounds are stretched to fill a
    /// <c>Min(width,height) · Scale</c> box centered at the layer position, so the
    /// geometry matches <see cref="GetLayerDeviceRect"/>. Falls back to the dashed
    /// placeholder when the symbol id is unknown or the font/glyph is missing.
    /// </summary>
    private static void DrawSymbolLayer(SKCanvas canvas, SymbolLayer layer, int width, int height)
    {
        var baseSize = Math.Min(width, height);
        var dstW = baseSize * (float)Math.Max(0.01, layer.EffectiveScaleX);
        var dstH = baseSize * (float)Math.Max(0.01, layer.EffectiveScaleY);
        var cx = width / 2f + layer.PositionX;
        var cy = height / 2f + layer.PositionY;
        var rect = new SKRect(cx - dstW / 2f, cy - dstH / 2f, cx + dstW / 2f, cy + dstH / 2f);

        DrawSymbolGlyph(
            canvas, layer.SymbolId, rect, layer.Tint.ToSKColor(),
            rotation: (float)layer.Rotation,
            outlined: layer.Outlined, outlineColor: layer.OutlineColor.ToSKColor(), outlineWidth: (float)layer.OutlineWidth,
            shadow: layer.Shadow, shadowColor: layer.ShadowColor.ToSKColor(), shadowBlur: (float)layer.ShadowBlur,
            shadowOffsetX: layer.ShadowOffsetX, shadowOffsetY: layer.ShadowOffsetY,
            useGradient: layer.UseGradient, gradientStart: layer.GradientStartColor.ToSKColor(),
            gradientEnd: layer.GradientEndColor.ToSKColor(), gradientAngle: (float)layer.GradientAngle);
    }

    /// <summary>
    /// Renders a symbol glyph (from <see cref="SymbolLibrary"/>) fitted into <paramref name="rect"/>,
    /// drawn shadow → outline → fill (solid tint or linear gradient). Shared by the symbol layer
    /// renderer and the plugin draw-canvas (<c>IRenderCanvas.DrawSymbol</c>, which passes tint only).
    /// Falls back to a dashed placeholder when the id is unknown or the font/glyph is missing.
    /// </summary>
    internal static void DrawSymbolGlyph(
        SKCanvas canvas, string symbolId, SKRect rect, SKColor tint,
        float rotation = 0,
        bool outlined = false, SKColor outlineColor = default, float outlineWidth = 0,
        bool shadow = false, SKColor shadowColor = default, float shadowBlur = 0,
        float shadowOffsetX = 0, float shadowOffsetY = 0,
        bool useGradient = false, SKColor gradientStart = default, SKColor gradientEnd = default,
        float gradientAngle = 0)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        if (!SymbolLibrary.TryGet(symbolId, out var def))
        {
            DrawSymbolPlaceholderRect(canvas, rect, tint);
            return;
        }

        var typeface = SymbolLibrary.GetTypeface();
        if (typeface == null)
        {
            DrawSymbolPlaceholderRect(canvas, rect, tint);
            return;
        }

        var glyph = SymbolLibrary.GlyphString(def.Codepoint);

        // A fixed reference size keeps the glyph path precise; the final size
        // comes from the matrix applied to the path below.
        const float refSize = 128f;
        using var font = new SKFont(typeface, refSize)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None
        };

        var glyphIds = font.GetGlyphs(glyph);
        if (glyphIds.Length == 0)
        {
            DrawSymbolPlaceholderRect(canvas, rect, tint);
            return;
        }

        using var path = font.GetGlyphPath(glyphIds[0]);
        if (path == null || path.IsEmpty)
        {
            DrawSymbolPlaceholderRect(canvas, rect, tint);
            return;
        }

        var gb = path.TightBounds;
        if (gb.Width <= 0 || gb.Height <= 0)
        {
            DrawSymbolPlaceholderRect(canvas, rect, tint);
            return;
        }

        var cx = rect.MidX;
        var cy = rect.MidY;
        var sx = rect.Width / gb.Width;
        var sy = rect.Height / gb.Height;

        // Transform the glyph path into device-pixel space:
        // translate(-tightCenter) -> scale -> rotate -> translate(cx,cy).
        var m = SKMatrix.CreateTranslation(cx, cy);
        m = SKMatrix.Concat(m, SKMatrix.CreateRotationDegrees(rotation));
        m = SKMatrix.Concat(m, SKMatrix.CreateScale(sx, sy));
        m = SKMatrix.Concat(m, SKMatrix.CreateTranslation(-gb.MidX, -gb.MidY));
        path.Transform(m);

        // 1) Drop shadow.
        if (shadow)
        {
            using var shadowPaint = new SKPaint
            {
                Color = shadowColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            if (shadowBlur > 0)
                shadowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadowBlur);

            var saved = canvas.Save();
            canvas.Translate(shadowOffsetX, shadowOffsetY);
            canvas.DrawPath(path, shadowPaint);
            canvas.RestoreToCount(saved);
        }

        // 2) Outline.
        if (outlined && outlineWidth > 0)
        {
            using var outlinePaint = new SKPaint
            {
                Color = outlineColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = outlineWidth,
                StrokeJoin = SKStrokeJoin.Round,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawPath(path, outlinePaint);
        }

        // 3) Fill (solid tint or linear gradient).
        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        SKShader gradientShader = null;
        if (useGradient)
        {
            var db = path.Bounds;
            var rad = gradientAngle * Math.PI / 180.0;
            var dx = (float)Math.Cos(rad);
            var dy = (float)Math.Sin(rad);
            var half = 0.5f * (Math.Abs(dx) * db.Width + Math.Abs(dy) * db.Height);
            if (half > 0)
            {
                var center = new SKPoint(db.MidX, db.MidY);
                var p0 = new SKPoint(center.X - dx * half, center.Y - dy * half);
                var p1 = new SKPoint(center.X + dx * half, center.Y + dy * half);
                gradientShader = SKShader.CreateLinearGradient(
                    p0, p1, new[] { gradientStart, gradientEnd }, null, SKShaderTileMode.Clamp);
            }
        }

        if (gradientShader != null)
            fillPaint.Shader = gradientShader;
        else
            fillPaint.Color = useGradient ? gradientStart : tint;

        canvas.DrawPath(path, fillPaint);
        gradientShader?.Dispose();
    }

    /// <summary>Fallback renderer: a dashed box in <paramref name="rect"/>, drawn when a symbol
    /// id is unknown or the icon font could not be loaded.</summary>
    private static void DrawSymbolPlaceholderRect(SKCanvas canvas, SKRect rect, SKColor tint)
    {
        using var paint = new SKPaint
        {
            Color = tint,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };
        canvas.DrawRect(rect, paint);
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
                // no change
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
            // *** Tile shader: imageScale takes effect via the scaledBmp size ***
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

    /// <summary>
    /// Renders a folder entry slot — wallpaper background extracted from the page (matching
    /// the slot's grid position), then optional image, then text.
    /// </summary>
    public static SKBitmap RenderFolderEntry(
        Services.FolderNavigation.FolderEntry entry,
        LoupedeckConfig config,
        int slotIndex,
        int width,
        int height,
        int gridColumns)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        DrawWallpaperOrColor(canvas, config, slotIndex, width, height, gridColumns, entry.BackColor);

        if (entry.Image != null)
        {
            var destRect = new SKRect(0, 0, width, height);
            using var scaledImage = ScaleAndPositionBitmap(entry.Image, width, height);
            canvas.DrawBitmap(scaledImage, destRect);
        }

        if (!string.IsNullOrEmpty(entry.Text))
        {
            DrawTextAt(
                canvas,
                entry.Text,
                entry.TextColor.ToSKColor(),
                entry.TextSize,
                centered: true,
                posX: 0,
                posY: 0,
                imageWidth: width,
                imageHeight: height,
                bold: entry.Bold);
        }

        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Renders the folder back-button slot — wallpaper background plus a centered chevron-left arrow.
    /// </summary>
    public static SKBitmap RenderFolderBackButton(
        LoupedeckConfig config,
        int slotIndex,
        int width,
        int height,
        int gridColumns)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        DrawWallpaperOrColor(canvas, config, slotIndex, width, height, gridColumns,
            Color.FromArgb(160, 0, 0, 0));

        // Draw a chevron-left arrow centered in the slot.
        using var arrowPaint = new SKPaint();
        arrowPaint.Color = SKColors.White;
        arrowPaint.Style = SKPaintStyle.Stroke;
        arrowPaint.StrokeWidth = 6;
        arrowPaint.IsAntialias = true;
        arrowPaint.StrokeCap = SKStrokeCap.Round;
        arrowPaint.StrokeJoin = SKStrokeJoin.Round;

        var cx = width / 2f;
        var cy = height / 2f;
        var size = Math.Min(width, height) * 0.30f;

        using var path = new SKPath();
        path.MoveTo(cx + size * 0.5f, cy - size);
        path.LineTo(cx - size * 0.5f, cy);
        path.LineTo(cx + size * 0.5f, cy + size);

        canvas.DrawPath(path, arrowPaint);

        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Renders an empty (disabled) folder slot — only the wallpaper cutout, no foreground.
    /// Visually communicates "no action here".
    /// </summary>
    public static SKBitmap RenderEmptyFolderSlot(
        LoupedeckConfig config,
        int slotIndex,
        int width,
        int height,
        int gridColumns)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        DrawWallpaperOrColor(canvas, config, slotIndex, width, height, gridColumns, Colors.Black);

        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Renders a side strip in segmented mode: the strip's full height is split into
    /// one region per knob on that dial column (3 × 60×90 on the Razer), each showing
    /// the knob's <see cref="RotaryButton.DisplayText"/> label centered. The background
    /// is the page wallpaper's true panel region for this strip (left = x 0–60, right =
    /// x 420–480 of the 480-wide panel) so the image stays continuous with the centre
    /// grid across the bezel; falls back to a solid dark fill when no wallpaper is set.
    /// </summary>
    public static SKBitmap RenderRotaryStrip(
        RotaryButtonPage page,
        LoupedeckConfig config,
        int width,
        int height,
        RotarySide side,
        Func<int, LoupixDeck.PluginSdk.IRenderCanvas, bool> drawSegment = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var bitmap = new SKBitmap(width, height);

        lock (SkiaRenderGate.Sync)
        {
            using var canvas = new SKCanvas(bitmap);

            DrawStripWallpaperOrColor(canvas, config, side, width, height, SKColors.Black);

            var buttons = page.RotaryButtons;
            var count = buttons?.Count ?? 0;
            if (count == 0)
            {
                canvas.Flush();
                return bitmap;
            }

            var segmentHeight = height / (float)count;

            for (var i = 0; i < count; i++)
            {
                var top = i * segmentHeight;

                // A segment provider (segmented mode) may own this segment — e.g. an audio
                // dial's volume bar. It draws onto a canvas clipped to the segment; if it
                // returns true we skip the label, otherwise fall through to the dial label.
                if (drawSegment != null)
                {
                    var rc = new SkiaRenderCanvas(canvas, width, (int)Math.Round(segmentHeight), 0, top);
                    bool drawn;
                    try { drawn = drawSegment(i, rc); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RenderRotaryStrip: segment {i} override failed: {ex.Message}");
                        drawn = false;
                    }
                    if (drawn) continue;
                }

                var text = buttons[i]?.DisplayText;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                DrawTextAt(
                    canvas,
                    text,
                    SKColors.White,
                    16,
                    centered: true,
                    posX: 0,
                    posY: top,
                    imageWidth: width,
                    imageHeight: segmentHeight,
                    bold: false);
            }

            canvas.Flush();
        }

        return bitmap;
    }

    /// <summary>
    /// Composites two side-strip bitmaps into a single frame for a swipe-follow
    /// animation. <paramref name="current"/> is drawn shifted vertically by
    /// <paramref name="offsetY"/> and <paramref name="neighbor"/> fills the gap it
    /// leaves: a negative offset (finger moving up, paging to the next page) reveals
    /// the neighbor entering from below; a positive offset (finger down, previous page)
    /// reveals it from above. <paramref name="neighbor"/> may be null (no adjacent
    /// page) — then only the shifted current page is drawn over black.
    /// </summary>
    public static SKBitmap ComposeVerticalSlide(SKBitmap current, SKBitmap neighbor, int offsetY)
    {
        ArgumentNullException.ThrowIfNull(current);

        var width = current.Width;
        var height = current.Height;
        var bitmap = new SKBitmap(width, height);

        lock (SkiaRenderGate.Sync)
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(current, 0, offsetY);
            if (neighbor != null)
            {
                var neighborY = offsetY < 0 ? offsetY + height : offsetY - height;
                canvas.DrawBitmap(neighbor, 0, neighborY);
            }
            canvas.Flush();
        }

        return bitmap;
    }

    // The unified panel is 480px wide; the side strips occupy its outer 60px columns.
    public const int PanelWidth = 480;
    public const int PanelHeight = 270;
    private const int StripWidth = 60;

    /// <summary>
    /// Renders a side strip in free-draw mode: the strip's wallpaper region as the
    /// background, with the page's <see cref="RotaryButtonPage.StripCanvas"/> layers
    /// (image/text/symbol) composited on top across the full 60×270 surface. The dials
    /// are decoupled — no per-knob labels are drawn. Falls back to just the background
    /// when the canvas is empty.
    /// </summary>
    public static SKBitmap RenderStripCanvas(
        TouchButton canvas,
        LoupedeckConfig config,
        int width,
        int height,
        RotarySide side)
    {
        var bitmap = new SKBitmap(width, height);

        lock (SkiaRenderGate.Sync)
        {
            using var skCanvas = new SKCanvas(bitmap);

            // No wallpaper → fall back to the canvas's own background colour (default black),
            // matching touch-button behaviour. Avoids sending a washed-out grey to the device.
            var fallback = canvas?.BackColor.ToSKColor() ?? SKColors.Black;
            DrawStripWallpaperOrColor(skCanvas, config, side, width, height, fallback);

            if (canvas?.Layers != null)
                DrawLayers(skCanvas, canvas.Layers, width, height);

            skCanvas.Flush();
        }

        return bitmap;
    }

    /// <summary>
    /// Fills a side strip with its true region of the page wallpaper (left = panel
    /// x 0–60, right = x 420–480), scaled from the stored 480×270 wallpaper, with the
    /// page's opacity dim on top. Falls back to <paramref name="fallbackColor"/> (black,
    /// or the free-draw canvas's own background) when no wallpaper is set.
    /// </summary>
    private static void DrawStripWallpaperOrColor(
        SKCanvas canvas,
        LoupedeckConfig config,
        RotarySide side,
        int width,
        int height,
        SKColor fallbackColor)
    {
        // A dedicated side-display wallpaper overdraws the main wallpaper's region.
        var (sideWallpaper, sideOpacity) = ResolveSideWallpaper(config, side, width, height);
        if (sideWallpaper != null)
        {
            var sideDest = new SKRect(0, 0, width, height);
            canvas.DrawBitmap(sideWallpaper, sideDest);
            if (sideOpacity > 0)
            {
                using var dim = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 * sideOpacity)) };
                canvas.DrawRect(sideDest, dim);
            }
            return;
        }

        var (wallpaper, opacity) = ResolveWallpaper(config);

        if (wallpaper == null)
        {
            canvas.Clear(fallbackColor);
            return;
        }

        // Panel-space x where this strip starts: left at 0, right at 480-60=420.
        var panelStartX = side == RotarySide.Right ? PanelWidth - StripWidth : 0;
        var scaleX = wallpaper.Width / (float)PanelWidth;
        var scaleY = wallpaper.Height / (float)height; // strip height == panel height (270)

        var srcRect = new SKRect(
            panelStartX * scaleX,
            0,
            (panelStartX + StripWidth) * scaleX,
            height * scaleY);
        var destRect = new SKRect(0, 0, width, height);

        canvas.DrawBitmap(wallpaper, srcRect, destRect);

        if (opacity > 0)
        {
            using var dim = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(255 * opacity)) };
            canvas.DrawRect(destRect, dim);
        }
    }

    private static void DrawWallpaperOrColor(
        SKCanvas canvas,
        LoupedeckConfig config,
        int slotIndex,
        int width,
        int height,
        int gridColumns,
        Color fallbackColor)
    {
        var (wallpaperToUse, opacityToUse) = ResolveWallpaper(config);

        if (wallpaperToUse != null && gridColumns > 0)
        {
            var col = slotIndex % gridColumns;
            var row = slotIndex / gridColumns;

            var srcRect = new SKRect(
                col * width,
                row * height,
                (col + 1) * width,
                (row + 1) * height);
            var destRect = new SKRect(0, 0, width, height);

            canvas.DrawBitmap(wallpaperToUse, srcRect, destRect);

            using var paint = new SKPaint();
            paint.Color = new SKColor(0, 0, 0, (byte)(255 * opacityToUse));
            canvas.DrawRect(destRect, paint);
        }
        else
        {
            canvas.Clear(fallbackColor.ToSKColor());
        }
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
    // Small reference-keyed LRU of decoded plugin images, so a plugin that holds a static image
    // and draws it every frame (via IRenderCanvas.DrawImage(byte[])) only pays the decode once.
    // Keyed by the byte[] instance (treat the array as immutable). All access + the Skia
    // Decode/Dispose happen under SkiaRenderGate.Sync (callers already hold it; the lock is
    // re-entrant), so evicted bitmaps are disposed without racing an active render.
    private const int ImageCacheCapacity = 32;
    private static readonly LinkedList<KeyValuePair<byte[], SKBitmap>> ImageLru = new();
    private static readonly Dictionary<byte[], LinkedListNode<KeyValuePair<byte[], SKBitmap>>> ImageCache
        = new(ReferenceEqualityComparer.Instance);

    /// <summary>Decodes <paramref name="bytes"/> to an <see cref="SKBitmap"/>, caching by array
    /// reference. The returned bitmap is owned by the cache — callers must NOT dispose it. Returns
    /// null on empty/undecodable input. Must be called under <see cref="SkiaRenderGate"/>.Sync.</summary>
    internal static SKBitmap DecodeCached(byte[] bytes)
    {
        if (bytes is not { Length: > 0 }) return null;

        lock (SkiaRenderGate.Sync)
        {
            if (ImageCache.TryGetValue(bytes, out var existing))
            {
                ImageLru.Remove(existing);
                ImageLru.AddFirst(existing);
                return existing.Value.Value;
            }

            var bmp = SKBitmap.Decode(bytes);
            if (bmp == null) return null;

            var node = ImageLru.AddFirst(new KeyValuePair<byte[], SKBitmap>(bytes, bmp));
            ImageCache[bytes] = node;

            while (ImageCache.Count > ImageCacheCapacity && ImageLru.Last is { } last)
            {
                ImageLru.RemoveLast();
                ImageCache.Remove(last.Value.Key);
                last.Value.Value.Dispose();
            }

            return bmp;
        }
    }

    /// <summary>Single-line advance width of <paramref name="text"/> in the UI font (no wrap).
    /// Backs <c>IRenderCanvas.MeasureText</c> so plugins can fit/ellipsize without their own Skia.</summary>
    internal static float MeasureTextWidth(string text, float fontSize, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        using var font = new SKFont(GetTextTypeface(bold, italic), fontSize);
        return font.MeasureText(text);
    }

    internal static void DrawTextAt(
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
        // Center maps to (Center, Middle); top-left to (Left, Top).
        DrawTextAligned(canvas, text, color, textSize, centered ? 1 : 0, centered ? 1 : 0,
            posX, posY, imageWidth, imageHeight, bold, italic, outlined, outlineColor);
    }

    /// <summary>
    /// Core wrapped-text renderer with independent horizontal (<paramref name="hAlign"/>:
    /// 0=Left, 1=Center, 2=Right) and vertical (<paramref name="vAlign"/>: 0=Top, 1=Middle,
    /// 2=Bottom) alignment within the box. Backs both <see cref="DrawTextAt"/> and the canvas
    /// alignment overload.
    /// </summary>
    internal static void DrawTextAligned(
        SKCanvas canvas, string text, SKColor color, float textSize,
        int hAlign, int vAlign,
        float posX = 0, float posY = 0, float imageWidth = 90, float imageHeight = 90,
        bool bold = false, bool italic = false, bool outlined = false, SKColor outlineColor = default)
    {
        if (canvas == null || string.IsNullOrEmpty(text))
            throw new ArgumentException("Canvas and text must not be null!");

        // Reuse the cached typeface; only the per-size SKFont is allocated here and
        // it is disposed at scope exit so it does not leak to the finalizer thread.
        var typeface = GetTextTypeface(bold, italic);

        using var font = new SKFont(typeface, textSize)
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
        var startY = vAlign switch
        {
            0 => posY - font.Metrics.Ascent,                                   // Top
            2 => posY + (imageHeight - totalHeight) - font.Metrics.Ascent,     // Bottom
            _ => posY + (imageHeight - totalHeight) / 2 - font.Metrics.Ascent  // Middle
        };

        // Draw every line
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            var textWidth = font.MeasureText(line);
            var drawX = hAlign switch
            {
                0 => posX,                                  // Left
                2 => posX + (imageWidth - textWidth),       // Right
                _ => posX + (imageWidth - textWidth) / 2f   // Center
            };

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

        // Honor explicit line breaks first — each segment is then width-wrapped
        // independently. Without this, '\n' (and '\r') would fall into the
        // space-split below and render as missing-glyph boxes.
        var segments = text.Replace("\r\n", "\n").Split('\n');
        if (segments.Length > 1)
        {
            foreach (var segment in segments)
                lines.AddRange(WrapText(segment, font, maxWidth));
            return lines;
        }

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