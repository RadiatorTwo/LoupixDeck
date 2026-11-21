using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.Actions;
using LoupixDeck.Services.AppActivation;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Puts a discovered application onto a touch button: the launch command, and the app's icon as a
/// symbol layer.
/// </summary>
public static class AppAssignment
{
    /// <summary>
    /// Rendered size of a freshly assigned app icon, in device pixels: inset from the key edge so
    /// the icon reads as an icon rather than as the whole key, and so a label added underneath
    /// still has room.
    /// </summary>
    public const int DefaultIconSizePx = 64;

    /// <summary>
    /// Converts <see cref="DefaultIconSizePx"/> into the <c>Scale</c> multiplier a layer persists.
    /// </summary>
    /// <remarks>
    /// For an icon layer the renderer resolves the long edge to
    /// <c>min(keyWidth, keyHeight) * Scale</c>, so the scale depends on the key actually being
    /// edited — key size is per-device and user-calibratable. Clamped to 1.0 so a key smaller than
    /// 64px gets a full-bleed icon instead of one hanging over the edge.
    /// </remarks>
    public static double DefaultIconScaleFor(int keySizePx) => IconScaleFor(DefaultIconSizePx, keySizePx);

    /// <summary>
    /// Rendered size of an app icon placed in one tap zone of a side strip, in device pixels: about
    /// three quarters of the 60px strip width, so the icon keeps a margin instead of filling the
    /// strip edge to edge.
    /// </summary>
    public const int StripIconSizePx = 47;

    /// <summary>
    /// Converts an icon size in device pixels into the <c>Scale</c> of an icon layer on a
    /// surface whose short edge is <paramref name="surfaceSizePx"/>, clamped like
    /// <see cref="DefaultIconScaleFor"/>.
    /// </summary>
    public static double IconScaleFor(int iconSizePx, int surfaceSizePx)
        => surfaceSizePx <= 0 ? 1.0 : Math.Clamp(iconSizePx / (double)surfaceSizePx, 0.05, 1.0);

    /// <summary>Builds the command string that launches <paramref name="app"/>, with the target
    /// escaped so it survives <see cref="CommandStringParser"/>. Single Instance and the running
    /// indicator start as set in Settings (issue #647), off unless the user turned them on there;
    /// the process name is filled in now, while discovery still knows the executable behind a
    /// launcher URI.</summary>
    public static string BuildLaunchCommand(InstalledApp app)
    {
        string processName = LaunchTargetProcess.Guess(app.Target, app.IconExtractSource);
        LayerDefaultSettings defaults = LayerDefaults.Current;
        return $"System.LaunchApp({CommandParameterEncoding.Encode(app.Target)}, {defaults.AppSingleInstance}, " +
               $"{CommandParameterEncoding.Encode(processName)}, {defaults.AppShowRunningIndicator})";
    }

    /// <summary>
    /// Assigns <paramref name="app"/> to <paramref name="button"/>. Returns the created icon layer,
    /// or null when no icon was available — the command is set either way.
    /// </summary>
    /// <remarks>
    /// <paramref name="iconRelativePath"/> is resolved by the caller, before this is called, so the
    /// button is never left half-applied: the icon either exists by the time anything is written, or
    /// the caller knowingly assigns a command without one. Resolving it here would mean extracting
    /// an icon — file IO and P/Invoke — in the middle of mutating the button, on the UI thread.
    /// </remarks>
    public static SymbolLayer ApplyToTouchButton(TouchButton button, InstalledApp app,
        string iconRelativePath, SKBitmap iconBitmap, bool replaceLayers, string layerName, int keySizePx)
    {
        if (button == null || app == null)
            return null;

        button.Command = BuildLaunchCommand(app);
        SymbolLayer layer = AddIconLayer(button, app, iconRelativePath, iconBitmap, replaceLayers, layerName,
            keySizePx);

        // A key's icon follows the defaults from Settings; a tap zone of a side strip, which calls
        // AddIconLayer directly, keeps its own size.
        LayerDefaults.ApplyToNewIcon(layer, keySizePx, keySizePx);
        ApplyPreferredLayout(button, layer, app.Name, keySizePx, keySizePx);
        return layer;
    }

    /// <summary>
    /// Lays the key out as the preferred layout template for apps from Settings (issue #624), with
    /// <paramref name="text"/> as the caption. Icon only, the default, leaves the icon as it was
    /// placed; the other layouts add the caption and arrange both like the button editor's layout
    /// templates, and text only drops the icon. Nothing happens without an icon, so a key whose app
    /// has none keeps its layers. Returns the caption, or null when none was added.
    /// </summary>
    /// <param name="uniqueName">Turns a base layer name into one no layer has yet; null keeps the text.</param>
    public static TextLayer ApplyPreferredLayout(TouchButton button, SymbolLayer icon, string text,
        int keyWidthPx, int keyHeightPx, Func<string, string> uniqueName = null)
    {
        ButtonTemplate template = LayerDefaults.AppLayout;
        if (button == null || icon == null || template == ButtonTemplate.IconOnly || string.IsNullOrWhiteSpace(text))
            return null;

        TextLayer caption = ActionAssignment.CreateCaption(text, keyWidthPx, keyHeightPx);
        caption.Name = uniqueName?.Invoke(text) ?? text;
        button.Layers.Add(caption);

        ButtonTemplatePlan plan = ActionAssignment.PlanTemplate(button, template, keyWidthPx, keyHeightPx);
        ActionAssignment.ApplyTemplate(button, plan, uniqueName);
        return caption;
    }

    /// <summary>
    /// Adds <paramref name="app"/>'s icon as a symbol layer to <paramref name="button"/> and leaves its
    /// command alone. Used where the launch command lives elsewhere, such as a side-strip tap zone.
    /// Returns the created layer, or null when no icon was available.
    /// </summary>
    public static SymbolLayer AddIconLayer(TouchButton button, InstalledApp app,
        string iconRelativePath, SKBitmap iconBitmap, bool replaceLayers, string layerName, int keySizePx)
    {
        if (button == null || app == null)
            return null;

        if (string.IsNullOrEmpty(iconRelativePath))
        {
            button.RewireLayerHandlers();
            return null;
        }

        if (replaceLayers)
            button.Layers.Clear();

        SymbolLayer layer = CreateIconSymbol(string.IsNullOrWhiteSpace(layerName) ? app.Name : layerName,
            iconRelativePath, iconBitmap, DefaultIconScaleFor(keySizePx));

        button.Layers.Add(layer);
        button.RewireLayerHandlers();
        return layer;
    }

    /// <summary>
    /// Builds a symbol layer that shows an application icon, the way an icon from an icon pack is
    /// placed: an app icon has a transparent background, so its visible part is a shape that takes
    /// Outline, Shadow, Tint and Gradient fill (issue #602). A colored icon keeps its own colors; a
    /// monochrome one, such as a symbolic Linux icon, is tinted like a glyph.
    /// </summary>
    /// <param name="size">Long edge of the layer box as a <see cref="LayerBase.Scale"/> multiplier.</param>
    public static SymbolLayer CreateIconSymbol(string name, string iconRelativePath, SKBitmap iconBitmap,
        double size)
    {
        SymbolLayer layer = new()
        {
            Name = name,
            IconAssetPath = iconRelativePath,
            KeepOriginalColors = iconBitmap != null && !IconColorAnalysis.IsMonochrome(iconBitmap)
        };
        layer.FitScaleToAspect(size, IconColorAnalysis.GetContentAspectRatio(iconBitmap) ?? 1.0);
        return layer;
    }
}
