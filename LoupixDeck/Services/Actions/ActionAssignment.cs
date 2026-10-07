using Avalonia.Media;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Actions;

/// <summary>
/// Puts a command onto a touch button the way the actions panel offers it: the command itself, its
/// Material Design glyph, and the action's name as a caption underneath.
/// </summary>
/// <remarks>
/// The sibling of <see cref="AppLauncher.AppAssignment"/> for everything that is not an installed
/// application. A command has no artwork of its own, so the button is built from the glyph the
/// command declares (<c>CommandAttribute.Icon</c>, surfaced through <c>MenuEntry.Icon</c>) plus a
/// text caption. Commands that declare no glyph get the caption alone, at a larger size. A plugin
/// command may ask for other layers through <c>CommandDescriptor.ButtonLayout</c>.
/// </remarks>
public static class ActionAssignment
{
    /// <summary>
    /// Key size the layout constants below are expressed in. Every pixel value is scaled from this
    /// reference onto the key actually being written, because key size is per-device and
    /// user-calibratable.
    /// </summary>
    private const int ReferenceKeySizePx = 90;

    /// <summary>Rendered size of the glyph on the reference key.</summary>
    private const int SymbolSizePx = 46;

    /// <summary>Glyph offset from the key centre, negative being upwards — it sits above the caption.</summary>
    private const int SymbolOffsetYPx = -8;

    /// <summary>The same two offsets mirrored, for the layout with the caption above the glyph.</summary>
    private const int SymbolOffsetYTopPx = 8;
    private const int LabelOffsetYTopPx = -27;

    /// <summary>Caption font size, offset from the key centre, and layout box, all on the reference key.</summary>
    private const int LabelTextSizePx = 11;
    private const int LabelOffsetYPx = 27;
    private const int LabelBoxWidthPx = 88;
    private const int LabelBoxHeightPx = 22;

    /// <summary>Caption metrics for a command with no glyph, where the text owns the whole key.</summary>
    private const int TextOnlySizePx = 14;
    private const int TextOnlyBoxPx = 84;

    /// <summary>The fraction of the key an icon fills when it stands alone, without a caption.</summary>
    private const double IconOnlyScale = 0.6;

    /// <summary>Text of a caption a template has to create because the button has no text layer.</summary>
    private const string DefaultCaptionText = "Text";

    /// <summary>
    /// The fraction of the key's short edge the glyph fills. A <see cref="LayerBase.Scale"/> is
    /// already relative to the surface, so unlike the pixel constants it needs no scaling.
    /// </summary>
    private const double SymbolScale = SymbolSizePx / (double)ReferenceKeySizePx;

    /// <summary>
    /// Assigns <paramref name="command"/> to <paramref name="button"/> and rebuilds its artwork from
    /// <paramref name="label"/> and <paramref name="symbolId"/>. Existing layers are replaced: an
    /// action brings its own complete look, and the caller confirms the replacement beforehand.
    /// </summary>
    /// <param name="symbolId">
    /// A <see cref="SymbolLibrary"/> id, or null. A glyph the library does not know is treated as
    /// absent rather than as an error, so a command declaring an icon we cannot resolve still
    /// lands on the button as a caption.
    /// </param>
    /// <param name="keyWidthPx">Width of the key being written, in device pixels.</param>
    /// <param name="keyHeightPx">Height of the key being written, in device pixels.</param>
    /// <param name="layout">
    /// The layers the command asks for, or null for the standard icon-and-caption look.
    /// </param>
    public static void ApplyToTouchButton(TouchButton button, string command, string label,
        string symbolId, int keyWidthPx, int keyHeightPx, ButtonLayoutDescriptor layout = null,
        IAssetService assets = null)
    {
        if (button == null || string.IsNullOrEmpty(command))
            return;

        button.Command = command;
        button.Layers.Clear();

        AddLayers(button, label, symbolId, keyWidthPx, keyHeightPx, layout, assets);
    }

    /// <summary>
    /// Appends the layers a command brings along to <paramref name="button"/>'s active state, leaving
    /// the command and every existing layer alone. The button editor uses this on an empty state.
    /// </summary>
    public static void AddLayers(TouchButton button, string label, string symbolId,
        int keyWidthPx, int keyHeightPx, ButtonLayoutDescriptor layout = null, IAssetService assets = null)
    {
        if (button == null)
            return;

        // The background is the button's own setting, so the user changes it like any other.
        AddLayers(button.Layers, background =>
        {
            button.BackColor = background;
            button.BackgroundEnabled = true;
        }, label, symbolId, keyWidthPx, keyHeightPx, layout, assets);

        button.RewireLayerHandlers();
    }

    /// <summary>
    /// Gives each state of <paramref name="button"/> that is still empty the layers (and background)
    /// its declared state brings along, matched by position. A state that already has layers is left
    /// alone, so the user's artwork is never replaced; a state without a layout is skipped.
    /// </summary>
    public static void AddStateLayouts(TouchButton button, IReadOnlyList<CommandStateInfo> states,
        string label, string symbolId, int keyWidthPx, int keyHeightPx, IAssetService assets = null)
    {
        if (button?.States == null || states == null)
            return;

        bool added = false;
        for (int index = 0; index < states.Count && index < button.States.Count; index++)
        {
            ButtonState state = button.States[index];
            ButtonLayoutDescriptor layout = states[index].Layout;
            if (layout == null || state.Layers.Count > 0)
                continue;

            AddLayers(state.Layers, background =>
            {
                state.BackColor = background;
                state.BackgroundEnabled = true;
            }, label, symbolId, keyWidthPx, keyHeightPx, layout, assets);
            added = true;
        }

        if (added)
            button.RewireLayerHandlers();
    }

    /// <summary>
    /// Appends a command's layers to <paramref name="layers"/> and hands a declared background
    /// colour to <paramref name="setBackground"/>; the caller rewires the button afterwards.
    /// </summary>
    private static void AddLayers(ICollection<LayerBase> layers, Action<Color> setBackground, string label,
        string symbolId, int keyWidthPx, int keyHeightPx, ButtonLayoutDescriptor layout, IAssetService assets)
    {
        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);
        string text = label ?? string.Empty;
        bool hasSymbol = !string.IsNullOrEmpty(symbolId) && SymbolLibrary.TryGet(symbolId, out _);

        switch (layout?.Mode ?? ButtonLayoutMode.Default)
        {
            case ButtonLayoutMode.None:
                break;

            case ButtonLayoutMode.IconOnly when hasSymbol:
                layers.Add(CreateSymbol(text, symbolId, 0, IconOnlyScale));
                break;

            case ButtonLayoutMode.CaptionOnly:
                layers.Add(CreateTextOnly(text, scaleX, scaleY));
                break;

            case ButtonLayoutMode.Custom:
                AddCustomLayers(layers, layout, text, symbolId, scaleX, scaleY, assets);
                break;

            // Default, IconAndCaption, and IconOnly for a command whose icon cannot be resolved:
            // the button must not end up empty, so it gets the standard look.
            default:
                if (hasSymbol)
                {
                    layers.Add(CreateSymbol(text, symbolId, Scaled(SymbolOffsetYPx, scaleY), SymbolScale));
                    layers.Add(CreateCaption(text, keyWidthPx, keyHeightPx));
                }
                else
                {
                    layers.Add(CreateTextOnly(text, scaleX, scaleY));
                }

                break;
        }

        if (Color.TryParse(layout?.BackgroundColor, out Color background))
            setBackground(background);
    }

    /// <summary>The caption under an icon, sized for a key of the given size.</summary>
    public static TextLayer CreateCaption(string text, int keyWidthPx, int keyHeightPx)
    {
        TextLayer caption = new()
        {
            Name = text,
            Text = text
        };
        PlaceCaption(caption, LabelOffsetYPx, keyWidthPx, keyHeightPx);
        return caption;
    }

    private static SymbolLayer CreateSymbol(string name, string symbolId, int positionY, double scale)
    {
        SymbolLayer symbol = new()
        {
            Name = name,
            SymbolId = symbolId
        };
        PlaceSymbol(symbol, positionY, scale);
        return symbol;
    }

    private static TextLayer CreateTextOnly(string text, double scaleX, double scaleY)
    {
        TextLayer layer = new()
        {
            Name = text,
            Text = text
        };
        PlaceTextOnly(layer, scaleX, scaleY);
        return layer;
    }

    /// <summary>Lays <paramref name="caption"/> out as the line of text next to an icon; the offset
    /// is the reference-key distance from the key centre, below (positive) or above (negative).</summary>
    private static void PlaceCaption(TextLayer caption, int offsetYPx, int keyWidthPx, int keyHeightPx)
    {
        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);
        caption.Centered = true;
        caption.TextSize = Scaled(LabelTextSizePx, scaleY);
        caption.PositionX = 0;
        caption.PositionY = Scaled(offsetYPx, scaleY);
        caption.BoxWidth = Scaled(LabelBoxWidthPx, scaleX);
        caption.BoxHeight = Scaled(LabelBoxHeightPx, scaleY);
        caption.Scale = 1.0;
        caption.ScaleY = 0;
    }

    /// <summary>Lays <paramref name="layer"/> out as the only content of the key.</summary>
    private static void PlaceTextOnly(TextLayer layer, double scaleX, double scaleY)
    {
        layer.Centered = true;
        layer.TextSize = Scaled(TextOnlySizePx, scaleY);
        layer.PositionX = 0;
        layer.PositionY = 0;
        layer.BoxWidth = Scaled(TextOnlyBoxPx, scaleX);
        layer.BoxHeight = Scaled(TextOnlyBoxPx, scaleY);
        layer.Scale = 1.0;
        layer.ScaleY = 0;
    }

    /// <summary>Centres <paramref name="symbol"/> horizontally at <paramref name="positionY"/> and
    /// fits it into a square of <paramref name="scale"/> of the key, keeping its own aspect ratio.</summary>
    private static void PlaceSymbol(SymbolLayer symbol, int positionY, double scale)
    {
        symbol.PositionX = 0;
        symbol.PositionY = positionY;

        if (symbol.IsImageIcon)
            symbol.FitScaleToAspect(scale, PackIconAspectRatio(symbol));
        else
            symbol.FitScaleToGlyph(scale);
    }

    /// <summary>Width / height of a pack icon's visible content, or the ratio its box already has
    /// when the asset cannot be loaded.</summary>
    private static double PackIconAspectRatio(SymbolLayer symbol)
    {
        if (BitmapHelper.AssetResolver?.Invoke(symbol.IconAssetPath) is { } bitmap)
        {
            SKRectI bounds = IconColorAnalysis.GetContentBounds(bitmap);
            if (bounds.Width > 0 && bounds.Height > 0)
                return (double)bounds.Width / bounds.Height;
        }

        return symbol.EffectiveScaleX / symbol.EffectiveScaleY;
    }

    /// <summary>
    /// Centres <paramref name="image"/> at <paramref name="positionY"/> with its longest edge at
    /// <paramref name="size"/> of the key's short edge. An image layer's <see cref="LayerBase.Scale"/>
    /// multiplies the picture after the renderer has fitted it into the whole key, so on a key that
    /// is not square the scale has to be derived from the picture's own size.
    /// </summary>
    private static void PlaceImage(ImageLayer image, int positionY, double size, int keyWidthPx, int keyHeightPx)
    {
        image.PositionX = 0;
        image.PositionY = positionY;
        image.ScaleY = 0;
        image.Scale = size;

        if (keyWidthPx <= 0 || keyHeightPx <= 0)
            return;

        double width;
        double height;
        if (!image.SourceRect.IsEmpty && image.SourceRect.Width > 0 && image.SourceRect.Height > 0)
        {
            width = image.SourceRect.Width;
            height = image.SourceRect.Height;
        }
        else if ((image.CachedImage ?? BitmapHelper.AssetResolver?.Invoke(image.AssetRelativePath)) is { Width: > 0, Height: > 0 } bitmap)
        {
            width = bitmap.Width;
            height = bitmap.Height;
        }
        else
        {
            return;
        }

        double fit = Math.Min(keyWidthPx / width, keyHeightPx / height);
        image.Scale = size * Math.Min(keyWidthPx, keyHeightPx) / (Math.Max(width, height) * fit);
    }

    /// <summary>Puts the icon layer at <paramref name="positionY"/> at <paramref name="size"/> of the key.</summary>
    private static void PlaceIcon(LayerBase icon, int positionY, double size, int keyWidthPx, int keyHeightPx)
    {
        switch (icon)
        {
            case SymbolLayer symbol:
                PlaceSymbol(symbol, positionY, size);
                break;
            case ImageLayer image:
                PlaceImage(image, positionY, size, keyWidthPx, keyHeightPx);
                break;
        }
    }

    /// <summary>
    /// The layers a template works with. Plugin layers and command-owned layers are never touched
    /// (a command-owned caption is only positioned); <paramref name="others"/> is everything else
    /// that is neither the icon nor the caption, which a template removes.
    /// </summary>
    private static void FindTemplateLayers(TouchButton button, out LayerBase icon, out TextLayer caption,
        out List<LayerBase> others)
    {
        icon = null;
        caption = null;
        others = [];

        List<LayerBase> layers = button.Layers?.ToList() ?? [];

        icon = layers.FirstOrDefault(l => l is SymbolLayer or ImageLayer && !l.IsCommandOwned);
        caption = layers.OfType<TextLayer>().FirstOrDefault(l => !l.IsCommandOwned)
                  ?? layers.OfType<TextLayer>().FirstOrDefault();

        foreach (LayerBase layer in layers)
        {
            if (layer is PluginLayer || layer.IsCommandOwned || ReferenceEquals(layer, icon) || ReferenceEquals(layer, caption))
                continue;

            others.Add(layer);
        }
    }

    /// <summary>
    /// The layers <see cref="ApplyTemplate"/> would remove from <paramref name="button"/>'s active
    /// state, so the editor can ask first. Empty when nothing would be removed or applying would do
    /// nothing at all.
    /// </summary>
    public static IReadOnlyList<LayerBase> GetLayersRemovedByTemplate(TouchButton button, ButtonTemplate template)
    {
        if (button == null)
            return [];

        FindTemplateLayers(button, out LayerBase icon, out TextLayer caption, out List<LayerBase> others);

        switch (template)
        {
            case ButtonTemplate.IconOnly:
                if (icon == null)
                    return [];

                if (caption is { IsCommandOwned: false })
                    others.Add(caption);
                break;

            case ButtonTemplate.TextOnly:
                if (icon != null)
                    others.Add(icon);
                break;
        }

        return others;
    }

    /// <summary>
    /// Rearranges the icon and caption already on <paramref name="button"/>'s active state into
    /// <paramref name="template"/>, with the same positions and sizes <see cref="AddLayers"/> gives a
    /// fresh button. The icon and caption layers are kept and only moved and resized, so their look
    /// and text stay; other layers go (see <see cref="GetLayersRemovedByTemplate"/>), except plugin
    /// layers and command-owned layers. A caption that is needed and missing is created through
    /// <paramref name="uniqueName"/>, which turns a base name into one no layer has yet.
    /// </summary>
    /// <returns>False when nothing was applied: an icon-only template on a button with no icon.</returns>
    public static bool ApplyTemplate(TouchButton button, ButtonTemplate template, int keyWidthPx, int keyHeightPx,
        Func<string, string> uniqueName = null)
    {
        if (button?.Layers == null)
            return false;

        FindTemplateLayers(button, out LayerBase icon, out TextLayer caption, out _);
        if (template == ButtonTemplate.IconOnly && icon == null)
            return false;

        foreach (LayerBase layer in GetLayersRemovedByTemplate(button, template))
            button.Layers.Remove(layer);

        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);

        // Without an icon the icon-and-caption templates have nothing to put the caption next to.
        if (icon == null && template != ButtonTemplate.IconOnly)
            template = ButtonTemplate.TextOnly;

        if (template != ButtonTemplate.IconOnly && caption == null)
        {
            caption = new TextLayer
            {
                Name = uniqueName?.Invoke(DefaultCaptionText) ?? DefaultCaptionText,
                Text = DefaultCaptionText
            };
            button.Layers.Add(caption);
        }

        switch (template)
        {
            case ButtonTemplate.IconCaptionBottom:
                PlaceIcon(icon, Scaled(SymbolOffsetYPx, scaleY), SymbolScale, keyWidthPx, keyHeightPx);
                PlaceCaption(caption, LabelOffsetYPx, keyWidthPx, keyHeightPx);
                break;

            case ButtonTemplate.IconCaptionTop:
                PlaceIcon(icon, Scaled(SymbolOffsetYTopPx, scaleY), SymbolScale, keyWidthPx, keyHeightPx);
                PlaceCaption(caption, LabelOffsetYTopPx, keyWidthPx, keyHeightPx);
                break;

            case ButtonTemplate.IconOnly:
                PlaceIcon(icon, 0, IconOnlyScale, keyWidthPx, keyHeightPx);
                break;

            case ButtonTemplate.TextOnly:
                PlaceTextOnly(caption, scaleX, scaleY);
                break;
        }

        button.RewireLayerHandlers();
        return true;
    }

    /// <summary>
    /// Builds the layers a plugin listed, bottom first. A layer that cannot be built — an unknown
    /// glyph, a kind this host does not know — is skipped rather than failing the assignment.
    /// </summary>
    private static void AddCustomLayers(ICollection<LayerBase> layers, ButtonLayoutDescriptor layout, string label,
        string symbolId, double scaleX, double scaleY, IAssetService assets)
    {
        foreach (ButtonLayerDescriptor descriptor in layout.Layers ?? [])
        {
            if (descriptor == null)
                continue;

            int x = Scaled(descriptor.OffsetX, scaleX);
            int y = Scaled(descriptor.OffsetY, scaleY);
            string name = string.IsNullOrEmpty(descriptor.Name) ? label : descriptor.Name;
            bool hasColor = Color.TryParse(descriptor.Color, out Color color);

            switch (descriptor.Kind)
            {
                case ButtonLayerKind.Symbol:
                    double iconScale = Math.Clamp(descriptor.IconScale, 0.1, 1.0);

                    // A picture the plugin brought wins over a glyph; when it cannot be stored or
                    // decoded, a glyph the plugin also named still gives the layer something to show.
                    if (TryImportPicture(assets, descriptor.ImageData, out string iconPath, out SKBitmap iconBitmap))
                    {
                        SymbolLayer pictureSymbol = new()
                        {
                            Name = name,
                            IconAssetPath = iconPath,
                            KeepOriginalColors = descriptor.KeepOriginalColors ?? !IconColorAnalysis.IsMonochrome(iconBitmap),
                            PositionX = x,
                            PositionY = y
                        };
                        SKRectI bounds = IconColorAnalysis.GetContentBounds(iconBitmap);
                        pictureSymbol.FitScaleToAspect(iconScale,
                            bounds.Height > 0 ? (double)bounds.Width / bounds.Height : 1.0);
                        if (hasColor && pictureSymbol.IsTintable)
                            pictureSymbol.Tint = color;

                        layers.Add(pictureSymbol);
                        break;
                    }

                    string id = symbolId;
                    if (!string.IsNullOrEmpty(descriptor.Glyph))
                    {
                        id = SymbolLibrary.TryGetByGlyph(descriptor.Glyph, out SymbolDefinition definition)
                            ? definition.Id
                            : null;
                    }

                    if (string.IsNullOrEmpty(id) || !SymbolLibrary.TryGet(id, out _))
                        break;

                    SymbolLayer symbol = CreateSymbol(name, id, y, iconScale);
                    symbol.PositionX = x;
                    if (hasColor)
                        symbol.Tint = color;

                    layers.Add(symbol);
                    break;

                case ButtonLayerKind.Image:
                    if (!TryImportPicture(assets, descriptor.ImageData, out string imagePath, out SKBitmap imageBitmap))
                        break;

                    layers.Add(new ImageLayer
                    {
                        Name = name,
                        AssetRelativePath = imagePath,
                        CachedImage = imageBitmap,
                        Scale = Math.Clamp(descriptor.IconScale, 0.1, 1.0),
                        PositionX = x,
                        PositionY = y
                    });
                    break;

                case ButtonLayerKind.Text:
                    TextLayer layer = new()
                    {
                        Name = name,
                        Text = descriptor.Text ?? label,
                        Centered = true,
                        TextSize = Scaled(descriptor.TextSize, scaleY),
                        PositionX = x,
                        PositionY = y,
                        // 0 stays 0: the box then fills the key.
                        BoxWidth = descriptor.BoxWidth > 0 ? Scaled(descriptor.BoxWidth, scaleX) : 0,
                        BoxHeight = descriptor.BoxHeight > 0 ? Scaled(descriptor.BoxHeight, scaleY) : 0,
                        TextSource = descriptor.TextSource switch
                        {
                            ButtonTextSource.Value => TextSource.DialValue,
                            ButtonTextSource.Detail => TextSource.ValueDetail,
                            _ => TextSource.Static
                        }
                    };
                    if (hasColor)
                        layer.TextColor = color;

                    layers.Add(layer);
                    break;

                case ButtonLayerKind.Indicator:
                    layers.Add(CreateIndicator(descriptor, name, x, y, hasColor ? color : null));
                    break;
            }
        }
    }

    /// <summary>An indicator arc from a plugin's layer; anything the plugin leaves unset keeps the layer's default.</summary>
    private static DialIndicatorLayer CreateIndicator(ButtonLayerDescriptor descriptor, string name, int x, int y,
        Color? fill)
    {
        DialIndicatorLayer indicator = new()
        {
            Name = name,
            Scale = Math.Clamp(descriptor.IconScale, 0.1, 1.0),
            PositionX = x,
            PositionY = y
        };
        if (fill is { } fillColor)
            indicator.FillColor = fillColor;
        if (Color.TryParse(descriptor.TrackColor, out Color track))
            indicator.TrackColor = track;
        if (descriptor.Thickness is > 0 and <= 0.5)
            indicator.Thickness = descriptor.Thickness.Value;
        if (descriptor.StartAngle is { } start)
            indicator.StartAngle = start;
        if (descriptor.SweepAngle is > 0 and <= 360)
            indicator.SweepAngle = descriptor.SweepAngle.Value;
        return indicator;
    }

    /// <summary>
    /// Stores a plugin's picture in the asset store and decodes it. False — and nothing written to
    /// the button — when there is no asset store, no data, a format the host does not know, or data
    /// that will not decode; a broken picture must not fail the assignment.
    /// </summary>
    private static bool TryImportPicture(IAssetService assets, byte[] data, out string relativePath, out SKBitmap bitmap)
    {
        relativePath = null;
        bitmap = null;

        string extension = DetectPictureExtension(data);
        if (assets == null || extension == null)
            return false;

        try
        {
            relativePath = assets.Import(data, extension, "plugin");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ActionAssignment] Storing a plugin picture failed: {ex.Message}");
            return false;
        }

        bitmap = string.IsNullOrEmpty(relativePath) ? null : assets.Load(relativePath);
        return bitmap != null;
    }

    /// <summary>The file extension for the picture's format, recognised from its bytes; null when
    /// it is none of SVG, PNG, JPEG, GIF or WebP.</summary>
    private static string DetectPictureExtension(byte[] data)
    {
        if (data == null || data.Length < 12)
            return null;

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return ".png";
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ".jpg";
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
            return ".gif";
        if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            return ".webp";

        // An SVG is text; the root element may follow an XML declaration, a doctype and comments.
        string head = System.Text.Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 2048));
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase) ? ".svg" : null;
    }

    /// <summary>
    /// Maps the reference key's pixel space onto a key of <paramref name="edgePx"/> pixels. A key
    /// size that is not known yet falls back to 1.0 rather than collapsing every layer onto a point.
    /// </summary>
    private static double ScaleFactor(int edgePx)
        => edgePx <= 0 ? 1.0 : edgePx / (double)ReferenceKeySizePx;

    /// <summary>Rounds a reference-key pixel value onto the real key, never below one pixel of
    /// magnitude so an offset or a box does not vanish on a small key.</summary>
    private static int Scaled(int referencePx, double factor)
    {
        int value = (int)Math.Round(referencePx * factor);
        if ((value == 0) && (referencePx != 0))
            return referencePx < 0 ? -1 : 1;

        return value;
    }
}
