using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.AppActivation;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Puts a discovered application onto a touch button: the launch command, and the app's icon as the
/// button's image.
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
    /// For a fitted image the renderer resolves the long edge to
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
    /// Converts an icon size in device pixels into the <c>Scale</c> of a fitted image layer on a
    /// surface whose short edge is <paramref name="surfaceSizePx"/>, clamped like
    /// <see cref="DefaultIconScaleFor"/>.
    /// </summary>
    public static double IconScaleFor(int iconSizePx, int surfaceSizePx)
        => surfaceSizePx <= 0 ? 1.0 : Math.Clamp(iconSizePx / (double)surfaceSizePx, 0.05, 1.0);

    /// <summary>Builds the command string that launches <paramref name="app"/>, with the target
    /// escaped so it survives <see cref="CommandStringParser"/>. Single Instance starts off — the
    /// button keeps launching every time until the user ticks it — but the process name is filled
    /// in now, while discovery still knows the executable behind a launcher URI.</summary>
    public static string BuildLaunchCommand(InstalledApp app)
    {
        string processName = LaunchTargetProcess.Guess(app.Target, app.IconExtractSource);
        return $"System.LaunchApp({CommandParameterEncoding.Encode(app.Target)}, False, " +
               $"{CommandParameterEncoding.Encode(processName)})";
    }

    /// <summary>
    /// Assigns <paramref name="app"/> to <paramref name="button"/>. Returns the created image layer,
    /// or null when no icon was available — the command is set either way.
    /// </summary>
    /// <remarks>
    /// <paramref name="iconRelativePath"/> is resolved by the caller, before this is called, so the
    /// button is never left half-applied: the icon either exists by the time anything is written, or
    /// the caller knowingly assigns a command without one. Resolving it here would mean extracting
    /// an icon — file IO and P/Invoke — in the middle of mutating the button, on the UI thread.
    /// </remarks>
    public static ImageLayer ApplyToTouchButton(TouchButton button, InstalledApp app,
        string iconRelativePath, bool replaceLayers, string layerName, int keySizePx)
    {
        if (button == null || app == null)
            return null;

        button.Command = BuildLaunchCommand(app);
        ImageLayer layer = AddIconLayer(button, app, iconRelativePath, replaceLayers, layerName, keySizePx);

        // A key's icon follows the defaults from Settings; a tap zone of a side strip, which calls
        // AddIconLayer directly, keeps its own size.
        LayerDefaults.ApplyToNewIcon(layer, keySizePx, keySizePx);
        return layer;
    }

    /// <summary>
    /// Adds <paramref name="app"/>'s icon as an image layer to <paramref name="button"/> and leaves its
    /// command alone. Used where the launch command lives elsewhere, such as a side-strip tap zone.
    /// Returns the created layer, or null when no icon was available.
    /// </summary>
    public static ImageLayer AddIconLayer(TouchButton button, InstalledApp app,
        string iconRelativePath, bool replaceLayers, string layerName, int keySizePx)
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

        ImageLayer layer = new()
        {
            Name = string.IsNullOrWhiteSpace(layerName) ? app.Name : layerName,
            AssetRelativePath = iconRelativePath,
            Scale = DefaultIconScaleFor(keySizePx)
        };

        button.Layers.Add(layer);
        button.RewireLayerHandlers();
        return layer;
    }
}