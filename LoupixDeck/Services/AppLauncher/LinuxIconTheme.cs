using System.Diagnostics;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Resolves a desktop entry's <c>Icon</c> theme name (e.g. <c>firefox</c>,
/// <c>org.gnome.Nautilus</c>) to an image file, the way the desktop's own menu finds it.
/// </summary>
/// <remarks>
/// Follows the XDG icon theme spec: the user's selected theme first, then the themes it inherits
/// from, then the <c>hicolor</c> fallback, then the legacy <c>pixmaps</c> directories. The first
/// theme that has the icon wins, and within it the largest size. Each theme's directory layout is
/// read from its <c>index.theme</c>, since themes differ (<c>48x48/apps</c> in hicolor,
/// <c>apps/48</c> in Mint-Y). Everything is indexed once per instance, so one discovery pass scans
/// the icon directories once.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxIconTheme
{
    private const string FallbackTheme = "hicolor";

    /// <summary>The size an SVG counts as when ranked against raster sizes. A scalable icon beats
    /// small bitmaps but loses to a genuinely large one, which renders without any rasterizing.</summary>
    private const int ScalableRank = 256;

    private readonly List<string> _iconRoots = [];
    private readonly List<string> _chain;
    private Dictionary<string, (string Path, int Rank)> _otherThemes;
    private readonly List<Dictionary<string, (string Path, int Rank)>> _themes = [];
    private readonly Dictionary<string, (string Path, int Rank)> _pixmaps = new(StringComparer.Ordinal);

    /// <param name="dataDirectories">XDG data directories in precedence order; earlier wins a tie.</param>
    public LinuxIconTheme(IEnumerable<string> dataDirectories)
    {
        string home = Environment.GetEnvironmentVariable("HOME")
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // The spec's search path: ~/.icons first, then icons/ under each data directory.
        _iconRoots.Add(Path.Combine(home, ".icons"));
        List<string> pixmapRoots = [];
        foreach (string root in dataDirectories)
        {
            _iconRoots.Add(Path.Combine(root, "icons"));
            pixmapRoots.Add(Path.Combine(root, "pixmaps"));
        }

        pixmapRoots.Add("/usr/share/pixmaps");

        _chain = ThemeChain(CurrentThemeName(home));
        foreach (string theme in _chain)
            _themes.Add(IndexTheme(theme));

        foreach (string directory in pixmapRoots.Distinct(StringComparer.Ordinal))
            IndexDirectory(directory, rank: 0, _pixmaps);
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

        foreach (Dictionary<string, (string Path, int Rank)> theme in _themes)
        {
            if (theme.TryGetValue(name, out (string Path, int Rank) hit))
                return hit.Path;
        }

        if (_pixmaps.TryGetValue(name, out (string Path, int Rank) pixmap))
            return pixmap.Path;

        // Not every application installs into hicolor (Linux Mint's Firefox only ships in the Mint
        // themes), and the selected theme cannot be read on every desktop. Any installed theme that
        // has the icon beats a blank button.
        _otherThemes ??= IndexOtherThemes();
        return _otherThemes.TryGetValue(name, out (string Path, int Rank) other) ? other.Path : null;
    }

    /// <summary>Every installed theme outside the selected chain, merged; the largest size wins.
    /// Built only on the first miss, so a desktop whose theme covers everything never pays for it.</summary>
    private Dictionary<string, (string Path, int Rank)> IndexOtherThemes()
    {
        Dictionary<string, (string Path, int Rank)> merged = new(StringComparer.Ordinal);
        HashSet<string> seen = new(_chain, StringComparer.Ordinal);

        foreach (string root in _iconRoots)
        {
            string[] themes;
            try
            {
                if (!Directory.Exists(root))
                    continue;

                themes = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[AppIcon] Cannot list '{root}': {ex.Message}");
                continue;
            }

            foreach (string theme in themes.Select(Path.GetFileName).Order(StringComparer.Ordinal))
            {
                if (!seen.Add(theme) || FindIndexFile(theme) == null)
                    continue;

                foreach ((string name, (string Path, int Rank) icon) in IndexTheme(theme))
                {
                    if (!merged.TryGetValue(name, out (string Path, int Rank) existing) || icon.Rank > existing.Rank)
                        merged[name] = icon;
                }
            }
        }

        return merged;
    }

    /// <summary>The selected theme, then everything it inherits from (depth first, as the spec
    /// orders it), with <c>hicolor</c> always last.</summary>
    private List<string> ThemeChain(string selected)
    {
        List<string> chain = [];
        if (selected != null)
            Visit(selected);

        chain.Remove(FallbackTheme);
        chain.Add(FallbackTheme);
        return chain;

        void Visit(string theme)
        {
            if (chain.Contains(theme, StringComparer.Ordinal) || FindIndexFile(theme) is not { } index)
                return;

            chain.Add(theme);
            if (ReadIndex(index).TryGetValue("Icon Theme", out Dictionary<string, string> header)
                && header.TryGetValue("Inherits", out string inherits))
            {
                foreach (string parent in SplitList(inherits))
                    Visit(parent);
            }
        }
    }

    private Dictionary<string, (string Path, int Rank)> IndexTheme(string theme)
    {
        Dictionary<string, (string Path, int Rank)> icons = new(StringComparer.Ordinal);

        foreach ((string subdirectory, int rank) in ThemeDirectories(theme))
        {
            foreach (string root in _iconRoots)
                IndexDirectory(Path.Combine(root, theme, subdirectory), rank, icons);
        }

        return icons;
    }

    /// <summary>A theme's icon directories and their ranks, as its <c>index.theme</c> lists them.
    /// A theme without one (hicolor when its package is missing) falls back to hicolor's
    /// <c>&lt;size&gt;/apps</c> layout.</summary>
    private IEnumerable<(string Subdirectory, int Rank)> ThemeDirectories(string theme)
    {
        if (FindIndexFile(theme) is not { } index)
            return GuessedDirectories(theme);

        Dictionary<string, Dictionary<string, string>> groups = ReadIndex(index);
        if (!groups.TryGetValue("Icon Theme", out Dictionary<string, string> header))
            return [];

        List<(string, int)> directories = [];
        IEnumerable<string> names = SplitList(header.GetValueOrDefault("Directories"))
            .Concat(SplitList(header.GetValueOrDefault("ScaledDirectories")))
            .Distinct(StringComparer.Ordinal);

        foreach (string name in names)
        {
            if (!groups.TryGetValue(name, out Dictionary<string, string> group))
                continue;

            int size = ParseInt(group.GetValueOrDefault("Size"), 0);
            int scale = ParseInt(group.GetValueOrDefault("Scale"), 1);
            bool scalable = string.Equals(group.GetValueOrDefault("Type"), "Scalable", StringComparison.Ordinal);
            directories.Add((name, scalable ? Math.Max(size * scale, ScalableRank) : size * scale));
        }

        return directories;
    }

    private IEnumerable<(string Subdirectory, int Rank)> GuessedDirectories(string theme)
    {
        List<(string, int)> directories = [];
        foreach (string root in _iconRoots)
        {
            string themeDirectory = Path.Combine(root, theme);
            if (!Directory.Exists(themeDirectory))
                continue;

            try
            {
                foreach (string sizeDirectory in Directory.GetDirectories(themeDirectory))
                {
                    string name = Path.GetFileName(sizeDirectory);
                    if (RankOf(name) is { } rank)
                        directories.Add((Path.Combine(name, "apps"), rank));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[AppIcon] Cannot list '{themeDirectory}': {ex.Message}");
            }
        }

        return directories;
    }

    private string FindIndexFile(string theme)
    {
        // A theme name comes from user settings or another theme's Inherits; never let it walk
        // out of the icon directories.
        if (string.IsNullOrWhiteSpace(theme) || theme.Contains('/') || theme.Contains(".."))
            return null;

        foreach (string root in _iconRoots)
        {
            string index = Path.Combine(root, theme, "index.theme");
            if (File.Exists(index))
                return index;
        }

        return null;
    }

    private static void IndexDirectory(string directory, int rank, Dictionary<string, (string Path, int Rank)> icons)
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
            // XPM is legal in themes and pixmaps but nothing in the render path can decode it.
            if (!HasImageExtension(file))
                continue;

            string name = Path.GetFileNameWithoutExtension(file);
            int fileRank = string.Equals(Path.GetExtension(file), ".svg", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(rank, ScalableRank)
                : rank;

            if (!icons.TryGetValue(name, out (string Path, int Rank) existing) || fileRank > existing.Rank)
                icons[name] = (file, fileRank);
        }
    }

    /// <summary>Rank of a hicolor-style size directory: its pixel size (<c>48x48</c>,
    /// <c>48x48@2</c>), <see cref="ScalableRank"/> for <c>scalable</c>, null for one to skip.</summary>
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

    /// <summary>
    /// The icon theme the desktop is set to, or null when it cannot be told. KDE keeps it in
    /// <c>kdeglobals</c>; the GTK desktops in gsettings, each under its own schema.
    /// </summary>
    private static string CurrentThemeName(string home)
    {
        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToUpperInvariant() ?? string.Empty;

        if (desktop.Contains("KDE"))
        {
            string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string kdeglobals = Path.Combine(
                !string.IsNullOrWhiteSpace(configHome) ? configHome : Path.Combine(home, ".config"), "kdeglobals");

            if (File.Exists(kdeglobals)
                && ReadIndex(kdeglobals).TryGetValue("Icons", out Dictionary<string, string> icons)
                && icons.TryGetValue("Theme", out string theme))
            {
                return theme;
            }

            return "breeze";
        }

        string schema = desktop.Contains("CINNAMON") ? "org.cinnamon.desktop.interface"
            : desktop.Contains("MATE") ? "org.mate.interface"
            : "org.gnome.desktop.interface";

        return ReadGSetting(schema, "icon-theme");
    }

    private static string ReadGSetting(string schema, string key)
    {
        try
        {
            ProcessStartInfo info = new("gsettings")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add("get");
            info.ArgumentList.Add(schema);
            info.ArgumentList.Add(key);

            using Process process = Process.Start(info);
            if (process == null)
                return null;

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000) || process.ExitCode != 0)
                return null;

            // gsettings prints a GVariant string: 'Mint-Y-Sand'.
            string value = output.Trim().Trim('\'');
            return value.Length > 0 ? value : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"[AppIcon] Cannot read the icon theme from gsettings: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads an INI-style file (<c>index.theme</c>, <c>kdeglobals</c>) into groups of
    /// key/value pairs. Unreadable files read as empty.</summary>
    private static Dictionary<string, Dictionary<string, string>> ReadIndex(string file)
    {
        Dictionary<string, Dictionary<string, string>> groups = new(StringComparer.Ordinal);
        Dictionary<string, string> current = null;

        try
        {
            foreach (string raw in File.ReadLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    groups.TryAdd(line[1..^1], current);
                    continue;
                }

                int equals = line.IndexOf('=');
                if (current != null && equals > 0)
                    current.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AppIcon] Cannot read '{file}': {ex.Message}");
        }

        return groups;
    }

    private static IEnumerable<string> SplitList(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, out int parsed) ? parsed : fallback;

    private static bool HasImageExtension(string file)
    {
        string extension = Path.GetExtension(file);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase);
    }
}