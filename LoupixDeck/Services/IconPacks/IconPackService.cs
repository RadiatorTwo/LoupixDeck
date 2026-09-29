using System.Collections.Concurrent;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.IconPacks;

/// <summary>
/// The icon packs the user added to the symbol picker, and the scans of their folders.
/// </summary>
/// <remarks>
/// Device-agnostic: packs are the same for every deck, so the service lives in the root container.
/// The registry is its own file (<c>icon-packs.json</c>), never part of a device config, so nothing
/// that reads, migrates or exports the configs is affected by it.
/// </remarks>
public interface IIconPackService
{
    /// <summary>The registered packs, read from disk on first use.</summary>
    IReadOnlyList<IconPack> Packs { get; }

    /// <summary>Raised after a pack was added or removed.</summary>
    event Action PacksChanged;

    /// <summary>
    /// Registers the folder at <paramref name="folderPath"/> and saves. Returns the new pack, the
    /// already registered pack for the same folder, or null when the path is unusable.
    /// </summary>
    IconPack Add(string folderPath);

    /// <summary>Unregisters a pack and saves. Its files and the buttons using its icons are untouched.</summary>
    void Remove(string packId);

    IconPack Find(string packId);

    /// <summary>The last scan of a pack in this session, or null. Shown at once while a rescan runs.</summary>
    IconPackIndex GetCachedIndex(string packId);

    /// <summary>Scans the pack folder off the UI thread and caches the result.</summary>
    Task<IconPackIndex> ScanAsync(IconPack pack, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IIconPackService"/>
public sealed class IconPackService(IConfigService configService) : IIconPackService
{
    private const string FileName = "icon-packs.json";

    /// <summary>One stored pack. <see cref="Type"/> leaves room for other pack kinds (icon fonts).</summary>
    private sealed class Entry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; } = FolderType;
    }

    private sealed class RegistryFile
    {
        public int Version { get; set; } = 1;
        public List<Entry> Packs { get; set; } = [];
    }

    private const string FolderType = "folder";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, IconPackIndex> _indexCache = new();
    private List<IconPack> _packs;

    // Entries of pack types this version does not know, written back unchanged on save so a newer
    // version's packs survive a round trip through this one.
    private List<Entry> _foreignEntries = [];

    public event Action PacksChanged;

    public IReadOnlyList<IconPack> Packs
    {
        get
        {
            lock (_gate)
            {
                return [.. _packs ??= Load()];
            }
        }
    }

    public IconPack Add(string folderPath)
    {
        string path = Normalize(folderPath);
        if (path == null || !Directory.Exists(path))
            return null;

        IconPack pack;
        lock (_gate)
        {
            _packs ??= Load();

            IconPack existing = _packs.FirstOrDefault(p => PathComparer.Equals(Normalize(p.Path), path));
            if (existing != null)
                return existing;

            pack = new IconPack(Guid.NewGuid().ToString("N"), UniqueName(FolderName(path)), path);
            _packs.Add(pack);
            Save();
        }

        PacksChanged?.Invoke();
        return pack;
    }

    public void Remove(string packId)
    {
        lock (_gate)
        {
            _packs ??= Load();
            if (_packs.RemoveAll(p => p.Id == packId) == 0)
                return;

            _indexCache.TryRemove(packId, out _);
            Save();
        }

        PacksChanged?.Invoke();
    }

    public IconPack Find(string packId)
    {
        if (string.IsNullOrEmpty(packId))
            return null;

        lock (_gate)
        {
            return (_packs ??= Load()).FirstOrDefault(p => p.Id == packId);
        }
    }

    public IconPackIndex GetCachedIndex(string packId)
        => packId != null && _indexCache.TryGetValue(packId, out IconPackIndex index) ? index : null;

    public async Task<IconPackIndex> ScanAsync(IconPack pack, CancellationToken cancellationToken)
    {
        IconPackIndex index = await Task.Run(() => IconPackScanner.Scan(pack, cancellationToken), cancellationToken);
        _indexCache[pack.Id] = index;
        return index;
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int i = 2; _packs.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} ({i})";
        return name;
    }

    private static string FolderName(string path)
    {
        string name = Path.GetFileName(path);
        // A drive root has no file name; fall back to the path itself.
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length > (root?.Length ?? 0) ? Path.TrimEndingDirectorySeparator(full) : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsFolderPack(Entry entry)
        => string.Equals(entry.Type ?? FolderType, FolderType, StringComparison.OrdinalIgnoreCase);

    private List<IconPack> Load()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);
        if (!File.Exists(path))
            return [];

        try
        {
            // A damaged file is backed up by the config service and yields null: start empty rather
            // than keep the picker from opening. Unknown pack types are kept out for this version.
            RegistryFile file = configService.LoadConfig<RegistryFile>(path);
            List<Entry> entries = file?.Packs?.Where(e => e != null).ToList() ?? [];

            _foreignEntries = [.. entries.Where(e => !IsFolderPack(e))];
            return entries
                .Where(e => IsFolderPack(e) && !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Path))
                .Select(e => new IconPack(e.Id, string.IsNullOrWhiteSpace(e.Name) ? FolderName(e.Path) : e.Name, e.Path))
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IconPacks] '{path}' could not be read: {ex.Message}");
            return [];
        }
    }

    private void Save()
    {
        string path = FileDialogHelper.GetConfigPath(FileName);

        try
        {
            configService.SaveConfig(new RegistryFile
            {
                Packs = [.. _packs.Select(p => new Entry { Id = p.Id, Name = p.Name, Path = p.Path }), .. _foreignEntries]
            }, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IconPacks] '{path}' could not be written: {ex.Message}");
        }
    }
}
