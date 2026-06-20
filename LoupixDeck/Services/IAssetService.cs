using SkiaSharp;

namespace LoupixDeck.Services;

/// <summary>
/// Stores image assets referenced by touch-button layers next to the config file
/// (in a sibling "assets/" folder). Assets are content-addressed by SHA-256 so
/// identical imports are deduplicated.
/// </summary>
public interface IAssetService
{
    /// <summary>Absolute path to the assets root folder.</summary>
    string AssetsRoot { get; }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into the asset folder under a hashed
    /// filename and returns the relative path to use in a layer. Pass
    /// <paramref name="subFolder"/> to store the asset in a sub-folder of the asset
    /// root (e.g. "wallpapers") so different asset kinds stay separated.
    /// </summary>
    string Import(string sourcePath, string subFolder = null);

    /// <summary>
    /// Loads (and caches) the bitmap for the given relative asset path.
    /// Returns null if the file is missing or unreadable.
    /// </summary>
    SKBitmap Load(string relativePath);

    /// <summary>
    /// Resolves a stored relative asset path (e.g. "assets/screensavers/&lt;hash&gt;.mp4")
    /// to its absolute path on disk. Used by consumers that need the file itself rather
    /// than a decoded bitmap (e.g. handing a video to ffmpeg).
    /// </summary>
    string ResolveAbsolute(string relativePath);

    /// <summary>
    /// Removes asset files in the asset folder that are not in the provided
    /// referenced set. Intended to be called during save to keep the folder
    /// from growing unbounded.
    /// </summary>
    void Cleanup(IEnumerable<string> referencedRelativePaths);
}
