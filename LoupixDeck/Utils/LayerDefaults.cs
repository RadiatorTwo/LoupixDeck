using System.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json;

namespace LoupixDeck.Utils;

/// <summary>
/// Loads and saves the <see cref="LayerDefaultSettings"/> and puts them on new layers. A layer only
/// picks them up when it is created, so changing them later leaves existing layers alone. Every
/// helper does nothing while its group is turned off.
/// </summary>
public static class LayerDefaults
{
    private const string FileName = "layer-defaults.json";

    /// <summary>The key size the pixel values are stored for.</summary>
    private const double ReferenceKeySizePx = 90.0;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new ColorJsonConverter() }
    };

    private static readonly Lock LoadLock = new();
    private static LayerDefaultSettings _current;

    /// <summary>The settings, loaded on first use. Changing a value saves the file.</summary>
    public static LayerDefaultSettings Current
    {
        get
        {
            lock (LoadLock)
            {
                if (_current == null)
                {
                    _current = Load();
                    _current.Text.PropertyChanged += OnChanged;
                    _current.Icon.PropertyChanged += OnChanged;
                }

                return _current;
            }
        }
    }

    public static TextLayerDefaults Text => Current.Text;

    public static IconLayerDefaults Icon => Current.Icon;

    private static LayerDefaultSettings Load()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);
        try
        {
            if (File.Exists(path))
            {
                LayerDefaultSettings loaded =
                    JsonConvert.DeserializeObject<LayerDefaultSettings>(File.ReadAllText(path), SerializerSettings);
                if (loaded != null)
                {
                    loaded.Text ??= new TextLayerDefaults();
                    loaded.Icon ??= new IconLayerDefaults();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // A broken file must not stop the app: keep it for inspection and start from the built-in look.
            Console.WriteLine($"[LayerDefaults] Failed to load {path}: {ex.Message}");
            BackupCorruptedFile(path);
        }

        return new LayerDefaultSettings();
    }

    private static void OnChanged(object sender, PropertyChangedEventArgs e) => Save();

    private static void Save()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);
        try
        {
            string json = JsonConvert.SerializeObject(Current, SerializerSettings);

            // Atomic write: temp file first, then move into place, as the other stores do.
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LayerDefaults] Failed to save {path}: {ex.Message}");
        }
    }

    private static void BackupCorruptedFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, $"{path}.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LayerDefaults] Failed to backup corrupted file: {ex.Message}");
        }
    }

    private static double ScaleFactor(int edgePx) => edgePx <= 0 ? 1.0 : edgePx / ReferenceKeySizePx;

    /// <summary>The text size in reference-key pixels: the default when it is on, else <paramref name="builtInPx"/>.</summary>
    public static int TextSizeOr(int builtInPx) => Text.Enabled ? Math.Max(1, Text.TextSize) : builtInPx;

    /// <summary>The icon size as a share of the key: the default when it is on, else <paramref name="builtIn"/>.</summary>
    public static double IconSizeOr(double builtIn) => Icon.Enabled ? Math.Clamp(Icon.SizePercent, 5, 100) / 100.0 : builtIn;

    /// <summary>Gives <paramref name="layer"/> the default colour, weight, slant and outline.</summary>
    public static void ApplyTextStyle(TextLayer layer)
    {
        TextLayerDefaults text = Text;
        if (layer == null || !text.Enabled)
            return;

        layer.TextColor = text.TextColor;
        layer.Bold = text.Bold;
        layer.Italic = text.Italic;
        layer.Outlined = text.Outlined;
        layer.OutlineColor = text.OutlineColor;
    }

    /// <summary>Gives <paramref name="layer"/> the default tint, gradient, outline and shadow.</summary>
    public static void ApplyIconStyle(SymbolLayer layer)
    {
        IconLayerDefaults icon = Icon;
        if (layer == null || !icon.Enabled)
            return;

        layer.Tint = icon.Tint;
        layer.UseGradient = icon.UseGradient;
        layer.GradientStartColor = icon.GradientStartColor;
        layer.GradientEndColor = icon.GradientEndColor;
        layer.GradientAngle = icon.GradientAngle;
        layer.Outlined = icon.Outlined;
        layer.OutlineColor = icon.OutlineColor;
        layer.OutlineWidth = icon.OutlineWidth;
        layer.Shadow = icon.Shadow;
    }

    /// <summary>
    /// Sets up a text layer the button editor adds on its own: default size, position and style on a
    /// key of the given size. The layer's box is left as the editor made it.
    /// </summary>
    public static void ApplyToNewText(TextLayer layer, int keyWidthPx, int keyHeightPx)
    {
        TextLayerDefaults text = Text;
        if (layer == null || !text.Enabled)
            return;

        double scaleY = ScaleFactor(keyHeightPx);
        layer.TextSize = Math.Max(1, (int)Math.Round(text.TextSize * scaleY));
        layer.PositionX = (int)Math.Round(text.PositionX * ScaleFactor(keyWidthPx));
        layer.PositionY = (int)Math.Round(text.PositionY * scaleY);
        ApplyTextStyle(layer);
    }

    /// <summary>
    /// Sets up a symbol or image layer the button editor or the actions panel adds on its own: default
    /// size and position on a key of the given size, and for a symbol the default style. A symbol keeps
    /// the aspect ratio its box already has; an image is fitted by the renderer, so its scale is the size.
    /// </summary>
    public static void ApplyToNewIcon(LayerBase layer, int keyWidthPx, int keyHeightPx)
    {
        IconLayerDefaults icon = Icon;
        if (layer == null || !icon.Enabled)
            return;

        double size = IconSizeOr(1.0);
        switch (layer)
        {
            case SymbolLayer symbol:
                symbol.FitScaleToAspect(size, symbol.EffectiveScaleX / symbol.EffectiveScaleY);
                ApplyIconStyle(symbol);
                break;
            case ImageLayer image:
                image.Scale = size;
                image.ScaleY = 0;
                break;
            default:
                return;
        }

        layer.PositionX = (int)Math.Round(icon.PositionX * ScaleFactor(keyWidthPx));
        layer.PositionY = (int)Math.Round(icon.PositionY * ScaleFactor(keyHeightPx));
    }
}
