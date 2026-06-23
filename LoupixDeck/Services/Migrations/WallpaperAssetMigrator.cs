using System.Security.Cryptography;
using LoupixDeck.Utils;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v4 → v5: moves page wallpapers out of the config (stored inline
/// as Base64 PNG) into the asset folder, mirroring image layers. Each page's Base64
/// <c>Wallpaper</c> is decoded, written to <c>assets/wallpapers/&lt;sha256&gt;.png</c> next
/// to the config, and replaced by a <c>WallpaperAssetPath</c> relative reference plus default
/// scaling parameters. The stored bitmap was already baked to 480×270, so the default
/// Fit re-bake is a 1:1 no-op and the wallpaper looks identical.
/// </summary>
/// <remarks>
/// The legacy root-level <c>Wallpaper</c>/<c>WallpaperOpacity</c> (from before per-page
/// wallpapers existed) are folded into page 0 when it has none, then removed — this
/// replaces the runtime migration that previously lived in the controller.
/// </remarks>
public sealed class WallpaperAssetMigrator : IConfigMigration
{
    private const string AssetsFolderName = "assets";
    // Page wallpapers live in their own sub-folder — kept in sync with
    // TouchPageWallpaperSettingsViewModel.WallpapersSubFolder.
    private const string WallpapersFolderName = "wallpapers";

    public int FromVersion => 4;

    public void Apply(JObject root, string configFilePath)
    {
        var assetsDir = ResolveAssetsDir(configFilePath);
        var pages = root["TouchButtonPages"] as JArray;

        // Fold the legacy root-level wallpaper into page 0 before per-page processing.
        var rootWallpaper = root["Wallpaper"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(rootWallpaper) && pages is { Count: > 0 } &&
            pages[0] is JObject firstPage &&
            string.IsNullOrWhiteSpace(firstPage["Wallpaper"]?.Value<string>()))
        {
            firstPage["Wallpaper"] = rootWallpaper;
            if (firstPage["WallpaperOpacity"] == null)
                firstPage["WallpaperOpacity"] = root["WallpaperOpacity"]?.Value<double?>() ?? 0;
        }

        root.Remove("Wallpaper");
        root.Remove("WallpaperOpacity");

        if (pages != null)
        {
            foreach (var page in pages.OfType<JObject>())
                MigratePage(page, assetsDir);
        }

        root["Version"] = FromVersion + 1;
    }

    private static void MigratePage(JObject page, string assetsDir)
    {
        var base64 = page["Wallpaper"]?.Value<string>();
        page.Remove("Wallpaper");

        if (string.IsNullOrWhiteSpace(base64)) return;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch
        {
            return; // unreadable legacy data — simply drop the wallpaper
        }

        // Content-addressed name, matching AssetService's scheme so identical
        // wallpapers across pages share one file.
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var fileName = hash + ".png";
        var absolute = Path.Combine(assetsDir, fileName);
        if (!File.Exists(absolute))
            File.WriteAllBytes(absolute, bytes);

        page["WallpaperAssetPath"] = $"{AssetsFolderName}/{WallpapersFolderName}/{fileName}";
        // The migrated image is already 480×270 → Fit at 100% is a 1:1 no-op.
        page["WallpaperScaling"] = 100;
        page["WallpaperPositionX"] = 0;
        page["WallpaperPositionY"] = 0;
        page["WallpaperScalingOption"] = (int)BitmapHelper.ScalingOption.Fit;
    }

    private static string ResolveAssetsDir(string configFilePath)
    {
        var configDir = Path.GetDirectoryName(configFilePath) ?? Environment.CurrentDirectory;
        var dir = Path.Combine(configDir, AssetsFolderName, WallpapersFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
