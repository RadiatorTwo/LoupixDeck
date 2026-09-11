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
    /// Icon placement on the button. Slightly inset so it does not touch the edges, which reads
    /// better on the physical key than an edge-to-edge icon.
    /// </summary>
    private const double DefaultIconScale = 79.0 / 90.0;

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
        string iconRelativePath, bool replaceLayers, string layerName)
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
            Scale = DefaultIconScale
        };

        button.Layers.Add(layer);
        button.RewireLayerHandlers();
        return layer;
    }
}