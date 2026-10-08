using System.Collections.ObjectModel;
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

    /// <summary>Share of the key's short edge an image must cover in both directions to count as a background.</summary>
    private const double FullKeyImageShare = 0.98;

    /// <summary>String key of the text of a caption a template has to create because the button has no text layer.</summary>
    private const string DefaultCaptionKey = "TouchButton_Template_DefaultCaption";

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
    /// multiplies the picture after the renderer has fitted it into the whole key, so the scale is
    /// corrected by what the drawn picture is off; without a loadable picture it stays at
    /// <paramref name="size"/>.
    /// </summary>
    private static void PlaceImage(ImageLayer image, int positionY, double size, int keyWidthPx, int keyHeightPx)
    {
        image.PositionX = 0;
        image.PositionY = positionY;
        image.ScaleY = 0;
        image.Scale = size;

        if (BitmapHelper.GetLayerDeviceRect(image, keyWidthPx, keyHeightPx) is { Width: > 0, Height: > 0 } drawn)
            image.Scale = size * size * Math.Min(keyWidthPx, keyHeightPx) / Math.Max(drawn.Width, drawn.Height);
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
    /// The layers a template works with. Only visible layers count. The icon is the top-most symbol or
    /// image layer. A picture that fills the whole key is a background: it is kept as it is, and is the
    /// icon only when the button has no other. A command-owned icon or caption is used only when there
    /// is no other, and is positioned but never removed. Plugin layers, dial indicators, command-owned
    /// and hidden layers are never touched; <paramref name="others"/> is everything else that is
    /// neither the icon nor the caption, which a template removes.
    /// </summary>
    private static void FindTemplateLayers(IEnumerable<LayerBase> stateLayers, int keyWidthPx, int keyHeightPx,
        out LayerBase icon, out TextLayer caption, out List<LayerBase> others)
    {
        List<LayerBase> layers = stateLayers?.ToList() ?? [];
        List<LayerBase> visible = layers.Where(l => l is { Visible: true }).ToList();

        // Layers are drawn first to last, so the last one is on top.
        List<LayerBase> backgrounds = visible.Where(l => l is ImageLayer image && IsFullKeyImage(image, keyWidthPx, keyHeightPx)).ToList();
        List<LayerBase> icons = visible.Where(l => l is SymbolLayer or ImageLayer && !backgrounds.Contains(l)).ToList();
        icon = icons.LastOrDefault(l => !l.IsCommandOwned) ?? icons.LastOrDefault()
            ?? backgrounds.LastOrDefault(l => !l.IsCommandOwned) ?? backgrounds.LastOrDefault();

        List<TextLayer> captions = visible.OfType<TextLayer>().ToList();
        caption = captions.FirstOrDefault(l => !l.IsCommandOwned) ?? captions.FirstOrDefault();

        others = [];
        foreach (LayerBase layer in visible)
        {
            if (layer is PluginLayer or DialIndicatorLayer || layer.IsCommandOwned || ReferenceEquals(layer, icon)
                || ReferenceEquals(layer, caption) || backgrounds.Contains(layer))
                continue;

            others.Add(layer);
        }
    }

    /// <summary>
    /// Whether <paramref name="image"/> covers the key as a background picture does: drawn on a key of
    /// the given size, it spans the key's short edge in both directions. A picture that cannot be
    /// loaded is judged by its scale, which is what a square one comes to.
    /// </summary>
    private static bool IsFullKeyImage(ImageLayer image, int keyWidthPx, int keyHeightPx)
    {
        if (BitmapHelper.GetLayerDeviceRect(image, keyWidthPx, keyHeightPx) is not { } drawn)
            return image.EffectiveScaleX >= FullKeyImageShare && image.EffectiveScaleY >= FullKeyImageShare;

        double edge = Math.Min(keyWidthPx, keyHeightPx) * FullKeyImageShare;
        return edge > 0 && drawn.Width >= edge && drawn.Height >= edge;
    }

    /// <summary>
    /// Works out what <paramref name="template"/> does to <paramref name="button"/>'s active state on a
    /// key of the given size, without changing the button, so the editor can ask about the layers that
    /// go before it hands the plan to <see cref="ApplyTemplate"/>. The icon and caption layers are kept
    /// and only moved and resized; other layers go, except plugin layers, dial indicators, hidden
    /// layers and command-owned layers. A command-owned caption cannot be dropped, so icon-only then
    /// lays out like icon and text, and so does text-only over a command-owned icon.
    /// </summary>
    public static ButtonTemplatePlan PlanTemplate(TouchButton button, ButtonTemplate template, int keyWidthPx,
        int keyHeightPx)
        => PlanTemplate(button?.Layers, template, keyWidthPx, keyHeightPx, createCaption: true);

    /// <summary>
    /// The same for the layers of one state of a button, active or not. Without
    /// <paramref name="createCaption"/> a missing caption is never added: an icon without one is laid
    /// out as icon only, and a state with neither icon nor caption, or with no text for text only,
    /// is left as it is.
    /// </summary>
    private static ButtonTemplatePlan PlanTemplate(ObservableCollection<LayerBase> layers, ButtonTemplate template,
        int keyWidthPx, int keyHeightPx, bool createCaption)
    {
        if (layers == null)
            return new ButtonTemplatePlan();

        FindTemplateLayers(layers, keyWidthPx, keyHeightPx, out LayerBase icon, out TextLayer caption,
            out List<LayerBase> removed);
        bool hasIcon = icon != null;

        if (!createCaption && caption == null)
        {
            if (icon == null || template == ButtonTemplate.TextOnly)
                return new ButtonTemplatePlan();

            template = ButtonTemplate.IconOnly;
        }

        switch (template)
        {
            case ButtonTemplate.IconOnly:
                if (icon == null)
                    return new ButtonTemplatePlan();

                if (caption is { IsCommandOwned: false })
                {
                    removed.Add(caption);
                    caption = layers.OfType<TextLayer>().FirstOrDefault(l => l is { Visible: true, IsCommandOwned: true });
                }

                // A caption a command owns stays, so the enlarged icon would cover it.
                if (caption != null)
                    template = ButtonTemplate.IconCaptionBottom;
                break;

            case ButtonTemplate.TextOnly:
                if (icon is { IsCommandOwned: false })
                {
                    removed.Add(icon);
                    icon = layers.LastOrDefault(l => l is SymbolLayer or ImageLayer && l is { Visible: true, IsCommandOwned: true });
                }

                // Likewise an icon a command owns stays, so the text would cover it.
                if (icon != null)
                    template = ButtonTemplate.IconCaptionBottom;
                break;
        }

        // Without an icon the icon-and-caption templates have nothing to put the caption next to.
        if (icon == null)
            template = ButtonTemplate.TextOnly;

        return new ButtonTemplatePlan
        {
            Applies = true,
            HasIcon = hasIcon,
            Removed = removed,
            Layout = template,
            Icon = icon,
            Caption = caption,
            KeyWidthPx = keyWidthPx,
            KeyHeightPx = keyHeightPx
        };
    }

    /// <summary>
    /// Rearranges the icon and caption of <paramref name="button"/>'s active state as
    /// <paramref name="plan"/> says, with the same positions and sizes <see cref="AddLayers"/> gives a
    /// fresh button, so their look and text stay. A caption that is needed and missing is created
    /// through <paramref name="uniqueName"/>, which turns a base name into one no layer has yet.
    /// </summary>
    /// <returns>False when nothing was applied: an icon-only template on a button with no icon.</returns>
    public static bool ApplyTemplate(TouchButton button, ButtonTemplatePlan plan, Func<string, string> uniqueName = null)
    {
        if (button?.Layers == null || !ApplyTemplate(button.Layers, plan, uniqueName))
            return false;

        button.RewireLayerHandlers();
        return true;
    }

    /// <summary>The same for the layers of one state of a button; the caller rewires the layer handlers.</summary>
    private static bool ApplyTemplate(ObservableCollection<LayerBase> layers, ButtonTemplatePlan plan,
        Func<string, string> uniqueName)
    {
        if (plan is not { Applies: true })
            return false;

        foreach (LayerBase layer in plan.Removed)
            layers.Remove(layer);

        int keyWidthPx = plan.KeyWidthPx;
        int keyHeightPx = plan.KeyHeightPx;
        double scaleX = ScaleFactor(keyWidthPx);
        double scaleY = ScaleFactor(keyHeightPx);

        LayerBase icon = plan.Icon;
        TextLayer caption = plan.Caption;
        if (plan.Layout != ButtonTemplate.IconOnly && caption == null)
        {
            string text = Localization.Loc.Tr(DefaultCaptionKey);
            caption = new TextLayer
            {
                Name = uniqueName?.Invoke(text) ?? text,
                Text = text
            };
            layers.Add(caption);
        }

        switch (plan.Layout)
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

        return true;
    }

    /// <summary>
    /// Applies <paramref name="template"/> to every state of every touch button on <paramref name="pages"/>.
    /// Unlike the editor it never adds a caption: an icon without one is laid out as icon only, and a
    /// state with neither icon nor caption (empty, or only plugin output) is left as it is. So is a
    /// folder back slot. The active state of a button does not change.
    /// </summary>
    public static TemplateApplyResult ApplyTemplateToPages(IEnumerable<TouchButtonPage> pages, ButtonTemplate template,
        int keyWidthPx, int keyHeightPx)
    {
        int buttonsChanged = 0;
        int layersRemoved = 0;

        foreach (TouchButtonPage page in pages ?? [])
        {
            foreach (TouchButton button in page?.TouchButtons ?? [])
            {
                if (button == null || button.IsFolderBackSlot || button.States == null)
                    continue;

                bool changed = false;
                foreach (ButtonState state in button.States)
                {
                    if (state?.Layers == null)
                        continue;

                    ButtonTemplatePlan plan = PlanTemplate(state.Layers, template, keyWidthPx, keyHeightPx,
                        createCaption: false);
                    if (!ApplyTemplate(state.Layers, plan, uniqueName: null))
                        continue;

                    // Only this state's handlers are rewired, so the button's active state stays as it is.
                    state.RewireLayerHandlers();
                    changed = true;
                    layersRemoved += plan.Removed.Count;
                }

                if (changed)
                    buttonsChanged++;
            }
        }

        return new TemplateApplyResult(buttonsChanged, layersRemoved);
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
