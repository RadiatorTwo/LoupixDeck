using System.Runtime.Versioning;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Resolves a desktop entry's <c>Icon</c> theme name (e.g. <c>firefox</c>,
/// <c>org.gnome.Nautilus</c>) to an image file, the way the desktop's own menu finds it.
/// </summary>
/// <remarks>
/// Searches the <c>hicolor</c> fallback theme, which the icon theme spec requires every
/// application to install into, and the legacy <c>pixmaps</c> directories. The user's selected
/// theme is not consulted: it only restyles icons that hicolor already provides. The index is
/// built once per instance, so one discovery pass scans the icon directories once.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxIconTheme
{
    /// <summary>The size an SVG counts as when ranked against raster sizes. A scalable icon beats
    /// small bitmaps but loses to a genuinely large one, which renders without any rasterizing.</summary>
    private const int ScalableRank = 256;

    private readonly Dictionary<string, (string Path, int Rank)> _icons = new(StringComparer.Ordinal);

    /// <param name="dataDirectories">XDG data directories in precedence order; earlier wins a tie.</param>
    public LinuxIconTheme(IEnumerable<string> dataDirectories)
    {
        foreach (string root in dataDirectories)
        {
            IndexTheme(Path.Combine(root, "icons", "hicolor"));
            IndexDirectory(Path.Combine(root, "pixmaps"), rank: 0);
        }
    }

    /// <summary>Returns the best image file for <paramref name="icon"/>, or null when none is
    /// installed. An absolute path is returned as-is when it exists.</summary>
    public string Resolve(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;

        if (Path.IsPathRooted(icon))
            return File.Exists(icon) ? icon : null;

        // The spec forbids an extension on a theme name, but some entries carry one anyway.
        string name = HasImageExtension(icon) ? Path.GetFileNameWithoutExtension(icon) : icon;
        return _icons.TryGetValue(name, out (string Path, int Rank) hit) ? hit.Path : null;
    }

    private void IndexTheme(string themeDirectory)
    {
        string[] sizes;
        try
        {
            if (!Directory.Exists(themeDirectory))
                return;

            sizes = Directory.GetDirectories(themeDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppIcon] Cannot list '{themeDirectory}': {ex.Message}");
            return;
        }

        foreach (string sizeDirectory in sizes)
        {
            int? rank = RankOf(Path.GetFileName(sizeDirectory));
            if (rank != null)
                IndexDirectory(Path.Combine(sizeDirectory, "apps"), rank.Value);
        }
    }

    private void IndexDirectory(string directory, int rank)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory))
                return;

            files = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppIcon] Cannot list '{directory}': {ex.Message}");
            return;
        }

        foreach (string file in files)
        {
            // XPM is legal in pixmaps but nothing in the render path can decode it.
            if (!HasImageExtension(file))
                continue;

            string name = Path.GetFileNameWithoutExtension(file);
            int fileRank = string.Equals(Path.GetExtension(file), ".svg", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(rank, ScalableRank)
                : rank;

            if (!_icons.TryGetValue(name, out (string Path, int Rank) existing) || fileRank > existing.Rank)
                _icons[name] = (file, fileRank);
        }
    }

    /// <summary>Rank of a theme size directory: its pixel size (<c>48x48</c>, <c>48x48@2</c>),
    /// <see cref="ScalableRank"/> for <c>scalable</c>, null for one to skip (<c>symbolic</c>).</summary>
    private static int? RankOf(string sizeDirectory)
    {
        if (string.Equals(sizeDirectory, "scalable", StringComparison.Ordinal))
            return ScalableRank;

        string[] scaleParts = sizeDirectory.Split('@');
        string[] dimensions = scaleParts[0].Split('x');
        if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out int size))
            return null;

        int scale = 1;
        if (scaleParts.Length == 2 && !int.TryParse(scaleParts[1], out scale))
            return null;

        return size * scale;
    }

    private static bool HasImageExtension(string file)
    {
        string extension = Path.GetExtension(file);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase);
    }
}
