using Avalonia.Media;
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

        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);
        string text = label ?? string.Empty;
        bool hasSymbol = !string.IsNullOrEmpty(symbolId) && SymbolLibrary.TryGet(symbolId, out _);

        switch (layout?.Mode ?? ButtonLayoutMode.Default)
        {
            case ButtonLayoutMode.None:
                break;

            case ButtonLayoutMode.IconOnly when hasSymbol:
                button.Layers.Add(CreateSymbol(text, symbolId, 0, IconOnlyScale));
                break;

            case ButtonLayoutMode.CaptionOnly:
                button.Layers.Add(CreateTextOnly(text, scaleX, scaleY));
                break;

            case ButtonLayoutMode.Custom:
                AddCustomLayers(button, layout, text, symbolId, scaleX, scaleY, assets);
                break;

            // Default, IconAndCaption, and IconOnly for a command whose icon cannot be resolved:
            // the button must not end up empty, so it gets the standard look.
            default:
                if (hasSymbol)
                {
                    button.Layers.Add(CreateSymbol(text, symbolId, Scaled(SymbolOffsetYPx, scaleY), SymbolScale));
                    button.Layers.Add(new TextLayer
                    {
                        Name = text,
                        Text = text,
                        Centered = true,
                        TextSize = Scaled(LabelTextSizePx, scaleY),
                        PositionY = Scaled(LabelOffsetYPx, scaleY),
                        BoxWidth = Scaled(LabelBoxWidthPx, scaleX),
                        BoxHeight = Scaled(LabelBoxHeightPx, scaleY)
                    });
                }
                else
                {
                    button.Layers.Add(CreateTextOnly(text, scaleX, scaleY));
                }

                break;
        }

        button.RewireLayerHandlers();
    }

    private static SymbolLayer CreateSymbol(string name, string symbolId, int positionY, double scale)
    {
        SymbolLayer symbol = new()
        {
            Name = name,
            SymbolId = symbolId,
            PositionY = positionY
        };
        symbol.FitScaleToGlyph(scale);
        return symbol;
    }

    private static TextLayer CreateTextOnly(string text, double scaleX, double scaleY) => new()
    {
        Name = text,
        Text = text,
        Centered = true,
        TextSize = Scaled(TextOnlySizePx, scaleY),
        BoxWidth = Scaled(TextOnlyBoxPx, scaleX),
        BoxHeight = Scaled(TextOnlyBoxPx, scaleY)
    };

    /// <summary>
    /// Builds the layers a plugin listed, bottom first. A layer that cannot be built — an unknown
    /// glyph, a kind this host does not know — is skipped rather than failing the assignment.
    /// </summary>
    private static void AddCustomLayers(TouchButton button, ButtonLayoutDescriptor layout, string label,
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

                        button.Layers.Add(pictureSymbol);
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

                    button.Layers.Add(symbol);
                    break;

                case ButtonLayerKind.Image:
                    if (!TryImportPicture(assets, descriptor.ImageData, out string imagePath, out SKBitmap imageBitmap))
                        break;

                    button.Layers.Add(new ImageLayer
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
                        BoxHeight = descriptor.BoxHeight > 0 ? Scaled(descriptor.BoxHeight, scaleY) : 0
                    };
                    if (hasColor)
                        layer.TextColor = color;

                    button.Layers.Add(layer);
                    break;
            }
        }
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
