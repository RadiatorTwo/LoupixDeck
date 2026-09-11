using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
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
    public static double DefaultIconScaleFor(int keySizePx)
        => keySizePx <= 0 ? 1.0 : Math.Clamp(DefaultIconSizePx / (double)keySizePx, 0.05, 1.0);

    /// <summary>Builds the command string that launches <paramref name="app"/>, with the target
    /// escaped so it survives <see cref="CommandStringParser"/>.</summary>
    public static string BuildLaunchCommand(InstalledApp app)
        => $"System.LaunchApp({CommandParameterEncoding.Encode(app.Target)})";

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