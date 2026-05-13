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

    public string Import(string sourcePath)
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
        var targetAbsolute = Path.Combine(AssetsRoot, targetFileName);

        if (!File.Exists(targetAbsolute))
        {
            File.Copy(sourcePath, targetAbsolute, overwrite: false);
        }

        // Relative path is stored in the config so the folder remains portable.
        return Path.Combine(AssetsFolderName, targetFileName).Replace('\\', '/');
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

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in referencedRelativePaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            referenced.Add(Path.GetFileName(rel));
        }

        foreach (var file in Directory.EnumerateFiles(AssetsRoot))
        {
            var name = Path.GetFileName(file);
            if (referenced.Contains(name)) continue;

            try
            {
                File.Delete(file);
                // Drop any cached bitmaps that pointed at this asset.
                foreach (var key in _cache.Keys)
                {
                    if (string.Equals(Path.GetFileName(key), name, StringComparison.OrdinalIgnoreCase))
                        _cache.TryRemove(key, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AssetService: failed to delete orphan '{file}': {ex.Message}");
            }
        }
    }

    private string ResolveAbsolute(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return normalized;

        // Strip leading "assets/" if present — AssetsRoot already points there.
        var prefix = AssetsFolderName + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        return Path.Combine(AssetsRoot, normalized);
    }
}
