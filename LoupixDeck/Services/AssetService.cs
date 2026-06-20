using System.Collections.Concurrent;
using System.Security.Cryptography;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services;

public class AssetService : IAssetService
{
    private const string AssetsFolderName = "assets";

    private readonly ConcurrentDictionary<string, SKBitmap> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string AssetsRoot { get; }

    public AssetService()
    {
        // GetConfigPath("") returns the config directory (it creates it if missing).
        var configDir = Path.GetDirectoryName(FileDialogHelper.GetConfigPath("config.json"))
                        ?? Environment.CurrentDirectory;

        AssetsRoot = Path.Combine(configDir, AssetsFolderName);
        Directory.CreateDirectory(AssetsRoot);
    }

    public string Import(string sourcePath, string subFolder = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return null;

        string hash;
        using (var stream = File.OpenRead(sourcePath))
        using (var sha = SHA256.Create())
        {
            hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        ext = ext.ToLowerInvariant();

        var targetFileName = hash + ext;

        // Optional sub-folder lets callers keep asset kinds apart (e.g. page
        // wallpapers under "assets/wallpapers/"); the relative path stays portable.
        var targetDir = AssetsRoot;
        var relativeDir = AssetsFolderName;
        if (!string.IsNullOrWhiteSpace(subFolder))
        {
            var normalizedSub = subFolder.Replace('\\', '/').Trim('/');
            targetDir = Path.Combine(AssetsRoot, normalizedSub.Replace('/', Path.DirectorySeparatorChar));
            relativeDir = AssetsFolderName + "/" + normalizedSub;
            Directory.CreateDirectory(targetDir);
        }

        var targetAbsolute = Path.Combine(targetDir, targetFileName);

        if (!File.Exists(targetAbsolute))
        {
            File.Copy(sourcePath, targetAbsolute, overwrite: false);
        }

        // Relative path is stored in the config so the folder remains portable.
        return (relativeDir + "/" + targetFileName).Replace('\\', '/');
    }

    public SKBitmap Load(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        if (_cache.TryGetValue(relativePath, out var cached) && cached != null)
            return cached;

        var absolute = ResolveAbsolute(relativePath);
        if (!File.Exists(absolute)) return null;

        try
        {
            var bitmap = SKBitmap.Decode(absolute);
            if (bitmap != null)
                _cache[relativePath] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AssetService: failed to load '{absolute}': {ex.Message}");
            return null;
        }
    }

    public void Cleanup(IEnumerable<string> referencedRelativePaths)
    {
        if (!Directory.Exists(AssetsRoot)) return;

        // Match by full relative path (e.g. "assets/wallpapers/abc.png"), not just
        // the file name: with sub-folders two different assets can share a name, so
        // a name-only compare would wrongly keep an orphan alive.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in referencedRelativePaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            referenced.Add(NormalizeRelative(rel));
        }

        // Recurse so wallpapers (and any future sub-folders) are reached too.
        foreach (var file in Directory.EnumerateFiles(AssetsRoot, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelative(
                AssetsFolderName + "/" + Path.GetRelativePath(AssetsRoot, file).Replace('\\', '/'));
            if (referenced.Contains(relative)) continue;

            try
            {
                // Drop (and dispose) any cached bitmap that pointed at this asset
                // BEFORE deleting: on Windows a live SKBitmap can hold the file
                // open, which would make File.Delete throw and silently keep the
                // orphan around.
                foreach (var key in _cache.Keys)
                {
                    if (string.Equals(NormalizeRelative(key), relative, StringComparison.OrdinalIgnoreCase) &&
                        _cache.TryRemove(key, out var cached))
                    {
                        cached?.Dispose();
                    }
                }

                File.Delete(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AssetService: failed to delete orphan '{file}': {ex.Message}");
            }
        }

        // Remove now-empty sub-folders so the asset tree doesn't accumulate stale dirs.
        foreach (var dir in Directory.EnumerateDirectories(AssetsRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AssetService: failed to remove empty asset dir '{dir}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Normalises a stored relative asset path for comparison: forward slashes,
    /// trimmed, and guaranteed to carry the leading "assets/" prefix. Case is
    /// handled by the caller's <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    private static string NormalizeRelative(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim();
        var prefix = AssetsFolderName + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = prefix + normalized.TrimStart('/');
        return normalized;
    }

    public string ResolveAbsolute(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return normalized;

        // Strip leading "assets/" if present — AssetsRoot already points there.
        var prefix = AssetsFolderName + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        return Path.Combine(AssetsRoot, normalized);
    }
}
