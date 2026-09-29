using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace LoupixDeck.Services.IconPacks;

/// <summary>
/// Walks an icon pack folder recursively and builds its <see cref="IconPackIndex"/>. Only file
/// names, sizes and small metadata side files are read here; icons are decoded lazily by the
/// picker's thumbnail loader, so a scan stays cheap even for thousands of files.
/// </summary>
public static class IconPackScanner
{
    /// <summary>Upper bound on indexed icons, so pointing the picker at a whole drive stays usable.</summary>
    public const int MaxEntries = 20_000;

    /// <summary>Category of icons directly in the pack root, when the pack has subfolders.</summary>
    public const string RootCategoryKey = "\u0001root";

    /// <summary>Bitmaps above this size are photos or artwork, not icons.</summary>
    private const long MaxRasterBytes = 10L * 1024 * 1024;

    /// <summary>Bytes read from an SVG to tell an SVG font from an image.</summary>
    private const int SvgSniffBytes = 8 * 1024;

    private static readonly Dictionary<string, IconEntryKind> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = IconEntryKind.RasterFile,
        [".jpg"] = IconEntryKind.RasterFile,
        [".jpeg"] = IconEntryKind.RasterFile,
        [".webp"] = IconEntryKind.RasterFile,
        [".bmp"] = IconEntryKind.RasterFile,
        [".svg"] = IconEntryKind.SvgFile
    };

    /// <summary>Folders that belong to tooling, never to an icon set.</summary>
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__MACOSX", ".git"
    };

    public static IconPackIndex Scan(IconPack pack, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(pack.Path))
            return IconPackIndex.Missing(pack.Id);

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Skipping reparse points avoids symlink loops; on Linux dot-files count as hidden.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
        };

        List<IconPackEntry> entries = [];
        int skippedFonts = 0;
        bool truncated = false;

        foreach (string file in Directory.EnumerateFiles(pack.Path, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Extensions.TryGetValue(Path.GetExtension(file), out IconEntryKind kind))
                continue;

            string relative = Path.GetRelativePath(pack.Path, file).Replace('\\', '/');
            if (IsIgnored(relative))
                continue;

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (info.Length == 0 || (kind == IconEntryKind.RasterFile && info.Length > MaxRasterBytes))
                    continue;
            }
            catch (IOException)
            {
                continue;
            }

            if (kind == IconEntryKind.SvgFile && IsSvgFont(file))
            {
                skippedFonts++;
                continue;
            }

            if (entries.Count >= MaxEntries)
            {
                truncated = true;
                break;
            }

            entries.Add(CreateEntry(pack, file, relative, kind));
        }

        entries.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));

        ImmutableArray<string> categories = [.. entries
            .SelectMany(static e => e.Categories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

        return new IconPackIndex(pack.Id, false, [.. entries], categories.Length > 1 ? categories : [],
            skippedFonts, truncated);
    }

    private static bool IsIgnored(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        // AppleDouble resource forks ("._name.png") sit next to real files on exFAT/network shares.
        if (segments[^1].StartsWith("._", StringComparison.Ordinal))
            return true;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (IgnoredFolders.Contains(segments[i]))
                return true;
        }

        return false;
    }

    private static IconPackEntry CreateEntry(IconPack pack, string file, string relative, IconEntryKind kind)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        int slash = relative.LastIndexOf('/');
        string folder = slash > 0 ? relative[..slash] : string.Empty;

        (ImmutableArray<string> tags, ImmutableArray<string> sideCategories) = ReadSideFile(file);

        ImmutableArray<string> categories = !sideCategories.IsDefaultOrEmpty
            ? sideCategories
            : [folder.Length > 0 ? folder : RootCategoryKey];

        StringBuilder search = new();
        search.Append(name).Append(' ').Append(string.Join(' ', Tokenize(name))).Append(' ').Append(folder);
        foreach (string tag in tags)
            search.Append(' ').Append(tag);

        return new IconPackEntry(
            IconPackKey.Create(pack.Id, relative),
            relative,
            file,
            name,
            kind,
            categories,
            search.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Reads the optional metadata side file <c>&lt;name&gt;.json</c> that some icon sets (e.g. Lucide)
    /// ship next to each icon. Only string arrays <c>tags</c> and <c>categories</c> are used; anything
    /// missing or malformed is ignored.
    /// </summary>
    private static (ImmutableArray<string> Tags, ImmutableArray<string> Categories) ReadSideFile(string iconFile)
    {
        string sideFile = Path.ChangeExtension(iconFile, ".json");
        if (!File.Exists(sideFile))
            return ([], []);

        try
        {
            using FileStream stream = File.OpenRead(sideFile);
            if (stream.Length > 64 * 1024)
                return ([], []);

            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return ([], []);

            return (ReadStrings(document.RootElement, "tags"), ReadStrings(document.RootElement, "categories"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ([], []);
        }
    }

    private static ImmutableArray<string> ReadStrings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            return [];

        return [.. array.EnumerateArray()
            .Where(static e => e.ValueKind == JsonValueKind.String)
            .Select(static e => e.GetString()?.Trim())
            .Where(static s => !string.IsNullOrEmpty(s))];
    }

    /// <summary>
    /// Splits a file name into search words on separators and camelCase boundaries, so
    /// "arrowLeft-bold" is found by "left" and "bold".
    /// </summary>
    private static IEnumerable<string> Tokenize(string name)
    {
        StringBuilder current = new();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool separator = c is '-' or '_' or ' ' or '.';
            bool camelBreak = i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]);

            if ((separator || camelBreak) && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (!separator)
                current.Append(c);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    /// <summary>
    /// True for an SVG font (IcoMoon/Fontello exports): its root holds <c>&lt;font&gt;</c>/<c>&lt;glyph&gt;</c>
    /// definitions and draws nothing, so rendering it as an image would show an empty cell.
    /// </summary>
    private static bool IsSvgFont(string file)
    {
        try
        {
            using FileStream stream = File.OpenRead(file);
            byte[] buffer = new byte[SvgSniffBytes];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            string head = Encoding.UTF8.GetString(buffer, 0, read);

            return head.Contains("<font-face", StringComparison.OrdinalIgnoreCase) ||
                   head.Contains("<font ", StringComparison.OrdinalIgnoreCase) ||
                   head.Contains("<font>", StringComparison.OrdinalIgnoreCase) ||
                   head.Contains("<glyph ", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
