using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Resolves application icons, caching decoded bitmaps in memory and extracted images on disk.
/// </summary>
/// <remarks>
/// Platform-specific extraction sits behind <see cref="TryExtract"/>: Windows pulls the icon out of
/// the executable, Linux looks up the icon theme. Everything else — the caches, their keys and their
/// bounds — is shared.
/// </remarks>
public sealed class AppIconExtractor : IAppIconExtractor
{
    /// <summary>
    /// Keeps the picker's working set resident without letting a machine with thousands of
    /// applications grow the cache without limit. Entries are cheap (a scaled-down bitmap), so this
    /// is generous enough that scrolling never re-decodes.
    /// </summary>
    private const int MaxCachedBitmaps = 512;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, Bitmap> _bitmaps = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recent = new();

    private readonly string _cacheDirectory = FileDialogHelper.GetConfigPath("appicons");

    /// <summary>
    /// One gate per executable. Several panels stream their icons in at the same time, so without
    /// this two extractions of the same application race for the same cache file — the loser writes
    /// into a file the winner still holds open, which GDI+ reports as "a generic error". Serialised,
    /// the second caller simply finds the finished file in the cache.
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Bitmap> GetThumbnailAsync(InstalledApp app, int width,
        CancellationToken cancellationToken = default)
    {
        if (app == null || width <= 0)
            return null;

        string file = await GetIconFileAsync(app, cancellationToken);
        if (file == null)
            return null;

        string key = $"{file}|{width}";

        lock (_sync)
        {
            if (_bitmaps.TryGetValue(key, out Bitmap cached))
            {
                Touch(key);
                return cached;
            }
        }

        Bitmap decoded = await Task.Run(() => Decode(file, width), cancellationToken);
        if (decoded == null)
            return null;

        lock (_sync)
        {
            // A concurrent caller may have won the race; keep the one already published so callers
            // never hold a bitmap that is about to be dropped.
            if (_bitmaps.TryGetValue(key, out Bitmap existing))
            {
                decoded.Dispose();
                Touch(key);
                return existing;
            }

            _bitmaps[key] = decoded;
            _recent.AddFirst(key);
            Trim();
            return decoded;
        }
    }

    public Task<string> GetIconFileAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        if (app == null)
            return Task.FromResult<string>(null);

        // Steam artwork, a Store logo or an absolute .desktop icon needs no extraction.
        if (!string.IsNullOrEmpty(app.PreResolvedIcon) && File.Exists(app.PreResolvedIcon))
            return Task.FromResult(app.PreResolvedIcon);

        return Task.Run(() => Extract(app, cancellationToken), cancellationToken);
    }

    private string Extract(InstalledApp app, CancellationToken cancellationToken)
    {
        string source = app.IconExtractSource;
        if (string.IsNullOrWhiteSpace(source))
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gates.GetOrAdd(source, _ => new object()))
        {
            return ExtractCore(source);
        }
    }

    /// <summary>Extracts <paramref name="source"/> into the disk cache. Callers hold its gate.</summary>
    private string ExtractCore(string source)
    {
        try
        {
            // Keying on the write time as well as the path means an updated or reinstalled
            // application gets a fresh icon instead of the stale one being served forever.
            DateTime writeTime = File.GetLastWriteTimeUtc(source);
            string cached = Path.Combine(_cacheDirectory, $"{Fingerprint(source, writeTime)}.png");

            if (File.Exists(cached) && new FileInfo(cached).Length > 0)
                return cached;

            Directory.CreateDirectory(_cacheDirectory);
            return TryExtract(source, cached) ? cached : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppIcon] Cannot cache the icon for '{source}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Extracts the icon of <paramref name="source"/> into <paramref name="destination"/>
    /// as a PNG. Returns false when this platform cannot, or the file has no icon.</summary>
    private static bool TryExtract(string source, string destination)
    {
#if WINDOWS
        // This branch only compiles under the WINDOWS constant, but the analyzer needs an explicit
        // OS guard (the same reason Program.cs carries one).
        return OperatingSystem.IsWindows() && WindowsAppIcons.TryExtractPng(source, destination);
#else
        // Linux gets its icon from the desktop entry, already resolved during discovery; there is
        // nothing to pull out of an ELF binary.
        return false;
#endif
    }

    private static Bitmap Decode(string file, int width)
    {
        try
        {
            using FileStream stream = File.OpenRead(file);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"[AppIcon] Cannot decode '{file}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Marks a key as most recently used. Must be called under <see cref="_sync"/>.</summary>
    private void Touch(string key)
    {
        _recent.Remove(key);
        _recent.AddFirst(key);
    }

    /// <summary>Evicts least-recently-used entries. Must be called under <see cref="_sync"/>.</summary>
    private void Trim()
    {
        while (_recent.Count > MaxCachedBitmaps)
        {
            string oldest = _recent.Last!.Value;
            _recent.RemoveLast();

            if (_bitmaps.Remove(oldest, out Bitmap bitmap))
                bitmap.Dispose();
        }
    }

    private static string Fingerprint(string source, DateTime writeTime)
    {
        string material = $"{source.ToLowerInvariant()}|{writeTime.Ticks}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}