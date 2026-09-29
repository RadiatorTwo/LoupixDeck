using System.Collections.Immutable;

namespace LoupixDeck.Services.IconPacks;

/// <summary>
/// A folder of icon files the user added to the symbol picker. Only the reference is stored: the
/// folder stays where it is, and the picker copies a single icon into the asset store when it is
/// placed on a button, so buttons keep working after the folder is moved or deleted.
/// </summary>
/// <param name="Id">Stable id, used in picker keys and the remembered picker source.</param>
/// <param name="Name">Display name in the source drop-down; the folder name by default.</param>
/// <param name="Path">Absolute path of the pack's root folder.</param>
public sealed record IconPack(string Id, string Name, string Path);

/// <summary>How an icon entry is decoded.</summary>
public enum IconEntryKind
{
    /// <summary>A bitmap file (PNG, JPEG, WebP, BMP).</summary>
    RasterFile,

    /// <summary>A standalone SVG image (not an SVG font).</summary>
    SvgFile
}

/// <summary>One icon found in a pack.</summary>
/// <param name="Key">Picker key, see <see cref="IconPackKey"/>.</param>
/// <param name="RelativePath">Path below the pack root, with forward slashes.</param>
/// <param name="FullPath">Absolute path on disk.</param>
/// <param name="DisplayName">The file name without extension.</param>
/// <param name="Kind">How the file is decoded.</param>
/// <param name="Categories">Categories from a metadata side file, or the entry's subfolder.</param>
/// <param name="SearchText">Lower-case text the picker search matches against.</param>
public sealed record IconPackEntry(
    string Key,
    string RelativePath,
    string FullPath,
    string DisplayName,
    IconEntryKind Kind,
    ImmutableArray<string> Categories,
    string SearchText);

/// <summary>The result of scanning a pack folder.</summary>
/// <param name="PackId">The scanned pack.</param>
/// <param name="FolderMissing">True when the pack folder does not exist (any more).</param>
/// <param name="Entries">The icons found, sorted by relative path.</param>
/// <param name="Categories">
/// Distinct categories across all entries; empty when there is at most one, since a single category
/// filters nothing.
/// </param>
/// <param name="SkippedFontFiles">SVG fonts found and skipped; they hold many glyphs, not one image.</param>
/// <param name="Truncated">True when the scan stopped at <see cref="IconPackScanner.MaxEntries"/>.</param>
public sealed record IconPackIndex(
    string PackId,
    bool FolderMissing,
    ImmutableArray<IconPackEntry> Entries,
    ImmutableArray<string> Categories,
    int SkippedFontFiles,
    bool Truncated)
{
    public static IconPackIndex Missing(string packId) => new(packId, true, [], [], 0, false);
}

/// <summary>
/// Picker keys of pack icons: <c>pack:&lt;packId&gt;/&lt;relativePath&gt;</c>. Stored on a symbol layer
/// only to pre-select the icon when it is picked again; the layer renders from its copied asset.
/// </summary>
public static class IconPackKey
{
    private const string Prefix = "pack:";

    public static string Create(string packId, string relativePath) => $"{Prefix}{packId}/{relativePath}";

    public static bool TryParse(string key, out string packId, out string relativePath)
    {
        packId = null;
        relativePath = null;

        if (string.IsNullOrEmpty(key) || !key.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        int slash = key.IndexOf('/', Prefix.Length);
        if (slash <= Prefix.Length || slash == key.Length - 1)
            return false;

        packId = key[Prefix.Length..slash];
        relativePath = key[(slash + 1)..];
        return true;
    }
}
