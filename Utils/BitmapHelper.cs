using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
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

        // Determine which wallpaper to use: current page's or fallback to first page's
        SKBitmap wallpaperToUse = null;
        double opacityToUse = 0;

        if (config.CurrentTouchButtonPage != null)
        {
            // Try to use the current page's wallpaper
            if (config.CurrentTouchButtonPage.Wallpaper != null)
            {
                wallpaperToUse = config.CurrentTouchButtonPage.Wallpaper;
                opacityToUse = config.CurrentTouchButtonPage.WallpaperOpacity;
            }
            // Fallback to first page's wallpaper if current page has none
            else if (config.TouchButtonPages != null &&
                     config.TouchButtonPages.Count > 0 &&
                     config.TouchButtonPages[0].Wallpaper != null)
            {
                wallpaperToUse = config.TouchButtonPages[0].Wallpaper;
                opacityToUse = config.TouchButtonPages[0].WallpaperOpacity;
            }
        }

        if (wallpaperToUse != null && gridColumns > 0)
        {
            // Determine the position of the button in the grid
            var col = touchButton.Index % gridColumns;
            var row = touchButton.Index / gridColumns;

            // Calculate the section from the wallpaper
            var srcRect = new SKRect(
                col * width,
                row * height,
                (col + 1) * width,
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

        // Convert back to RenderTargetBitmap and save in the TouchButton
        // var rtb = bitmap.ToRenderTargetBitmap();
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
    public static SKBitmap RenderEditorCanvas(
        TouchButton touchButton,
        LoupedeckConfig config,
        int canvasSize = 600,
        int frameSize = 300)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        const int deviceSize = 90;

        var bmp = new SKBitmap(canvasSize, canvasSize);
        using var canvas = new SKCanvas(bmp);

        canvas.Clear(new SKColor(0x22, 0x22, 0x22));

        var frameOffset = (canvasSize - frameSize) / 2f;
        var frameRect = new SKRect(frameOffset, frameOffset,
            frameOffset + frameSize, frameOffset + frameSize);

        // Fill the frame with the button's background color (the device-pixel area).
        using (var bgPaint = new SKPaint { Color = touchButton.BackColor.ToSKColor() })
        {
            canvas.DrawRect(frameRect, bgPaint);
        }

        // Switch to device-pixel space inside the frame so layer positions/scale match
        // exactly what the device render produces.
        canvas.Save();
        canvas.Translate(frameOffset, frameOffset);
        canvas.Scale((float)frameSize / deviceSize);

        if (touchButton.Layers != null)
        {
            foreach (var layer in touchButton.Layers)
            {
                if (layer == null || !layer.Visible) continue;

                switch (layer)
                {
                    case ImageLayer image:
                        DrawImageLayerExtended(canvas, image, deviceSize, deviceSize);
                        break;
                    case TextLayer text:
                        DrawTextLayer(canvas, text, deviceSize, deviceSize);
                        break;
                    case SymbolLayer symbol:
                        DrawSymbolPlaceholder(canvas, symbol, deviceSize, deviceSize);
                        break;
                }
            }
        }

        canvas.Restore();

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
        int frameSize = 300)
    {
        if (layer == null || !layer.Visible) return null;

        const int deviceSize = 90;
        var deviceRect = GetLayerDeviceRect(layer, deviceSize, deviceSize);
        if (deviceRect == null) return null;

        var frameOffset = (canvasSize - frameSize) / 2f;
        var scale = (float)frameSize / deviceSize;
        var dr = deviceRect.Value;
        return new SKRect(
            frameOffset + dr.Left * scale,
            frameOffset + dr.Top * scale,
            frameOffset + dr.Right * scale,
            frameOffset + dr.Bottom * scale);
    }

    /// <summary>
    /// Returns the bounding rectangle of a layer in 90×90 device-pixel space.
    /// Mirrors the geometry math used in <see cref="DrawImageLayerExtended"/> /
    /// <see cref="DrawSymbolPlaceholder"/> so the selection overlay always matches
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
                var size = Math.Min(deviceW, deviceH) * 0.6f * (float)Math.Max(0.1, symbol.Scale);
                var cx = deviceW / 2f + symbol.PositionX;
                var cy = deviceH / 2f + symbol.PositionY;
                return new SKRect(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
            }
            case TextLayer text:
                return MeasureTextDeviceRect(text, deviceW, deviceH);
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
                    DrawSymbolPlaceholder(canvas, symbol, width, height);
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

    private static void DrawSymbolPlaceholder(SKCanvas canvas, SymbolLayer layer, int width, int height)
    {
        // Stub renderer: a dashed box with the symbol id (if any) until the
        // symbol library is wired up.
        using var paint = new SKPaint
        {
            Color = layer.Tint.ToSKColor(),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };

        var size = Math.Min(width, height) * 0.6f * (float)Math.Max(0.1, layer.Scale);
        var cx = width / 2f + layer.PositionX;
        var cy = height / 2f + layer.PositionY;
        var rect = new SKRect(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
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

    private static void DrawWallpaperOrColor(
        SKCanvas canvas,
        LoupedeckConfig config,
        int slotIndex,
        int width,
        int height,
        int gridColumns,
        Color fallbackColor)
    {
        SKBitmap wallpaperToUse = null;
        double opacityToUse = 0;

        if (config?.CurrentTouchButtonPage != null)
        {
            if (config.CurrentTouchButtonPage.Wallpaper != null)
            {
                wallpaperToUse = config.CurrentTouchButtonPage.Wallpaper;
                opacityToUse = config.CurrentTouchButtonPage.WallpaperOpacity;
            }
            else if (config.TouchButtonPages != null &&
                     config.TouchButtonPages.Count > 0 &&
                     config.TouchButtonPages[0].Wallpaper != null)
            {
                wallpaperToUse = config.TouchButtonPages[0].Wallpaper;
                opacityToUse = config.TouchButtonPages[0].WallpaperOpacity;
            }
        }

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