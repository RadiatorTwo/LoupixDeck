using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v5 → v6: nests each page's flat main-wallpaper fields
/// (<c>WallpaperAssetPath</c>, <c>WallpaperScaling</c>, <c>WallpaperPositionX/Y</c>,
/// <c>WallpaperScalingOption</c>, <c>WallpaperOpacity</c>) into a single
/// <c>MainWallpaper</c> object, matching the <see cref="LoupixDeck.Models.WallpaperSlot"/>
/// shape. This unifies the main panel wallpaper with the new optional left/right
/// side-display wallpapers, which share the same slot model. The left/right slots are
/// left absent so they keep their empty defaults after deserialization.
/// </summary>
public sealed class WallpaperSlotMigrator : IConfigMigration
{
    public int FromVersion => 5;

    public void Apply(JObject root, string configFilePath)
    {
        if (root["TouchButtonPages"] is JArray pages)
        {
            foreach (var page in pages.OfType<JObject>())
                MigratePage(page);
        }

        root["Version"] = FromVersion + 1;
    }

    private static void MigratePage(JObject page)
    {
        var main = new JObject
        {
            ["AssetPath"] = page["WallpaperAssetPath"],
            ["Scaling"] = page["WallpaperScaling"] ?? 100,
            ["PositionX"] = page["WallpaperPositionX"] ?? 0,
            ["PositionY"] = page["WallpaperPositionY"] ?? 0,
            ["ScalingOption"] = page["WallpaperScalingOption"] ?? 0,
            ["Opacity"] = page["WallpaperOpacity"] ?? 0,
            ["Mirror"] = false,
        };

        page.Remove("WallpaperAssetPath");
        page.Remove("WallpaperScaling");
        page.Remove("WallpaperPositionX");
        page.Remove("WallpaperPositionY");
        page.Remove("WallpaperScalingOption");
        page.Remove("WallpaperOpacity");

        page["MainWallpaper"] = main;
    }
}
