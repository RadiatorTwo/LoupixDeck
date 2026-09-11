using System.Globalization;
using System.Text;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Reader for an XDG desktop entry (<c>.desktop</c>), the way applications are described on Linux.
/// Only the <c>[Desktop Entry]</c> group is read; trailing action groups such as
/// <c>[Desktop Action new-window]</c> are ignored.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled rather than treated as an INI file: desktop entries allow '=' inside
/// values, use '#' comments only on their own line, and carry locale-suffixed keys
/// (<c>Name[de]</c>), none of which a generic INI reader handles the way the spec requires.
/// </remarks>
public sealed class DesktopEntry
{
    private const string EntryGroup = "[Desktop Entry]";

    private DesktopEntry(string filePath, Dictionary<string, string> values)
    {
        FilePath = filePath;
        Name = LocalizedValue(values, "Name") ?? string.Empty;
        GenericName = LocalizedValue(values, "GenericName");
        Comment = LocalizedValue(values, "Comment");
        Exec = Value(values, "Exec");
        TryExec = Value(values, "TryExec");
        Icon = Value(values, "Icon");
        Type = Value(values, "Type");
        Path = Value(values, "Path");
        NoDisplay = Flag(values, "NoDisplay");
        Hidden = Flag(values, "Hidden");
        Terminal = Flag(values, "Terminal");
        Categories = List(values, "Categories");
        OnlyShowIn = List(values, "OnlyShowIn");
        NotShowIn = List(values, "NotShowIn");
    }

    /// <summary>Absolute path of the <c>.desktop</c> file this was read from.</summary>
    public string FilePath { get; }

    /// <summary>Display name, preferring the entry for the current UI language.</summary>
    public string Name { get; }

    public string GenericName { get; }
    public string Comment { get; }

    /// <summary>Raw <c>Exec</c> value, still carrying its field codes. Use
    /// <see cref="TryBuildCommandLine"/> to turn it into something launchable.</summary>
    public string Exec { get; }

    /// <summary>Executable whose presence decides whether the entry is installed. Optional.</summary>
    public string TryExec { get; }

    /// <summary>Either an absolute file path or an icon-theme name.</summary>
    public string Icon { get; }

    /// <summary>Entry type. Only <c>Application</c> is launchable.</summary>
    public string Type { get; }

    /// <summary>Working directory the application asks to be started in. Optional.</summary>
    public string Path { get; }

    /// <summary>The entry exists but should not be shown in a menu.</summary>
    public bool NoDisplay { get; }

    /// <summary>The entry is to be treated as deleted.</summary>
    public bool Hidden { get; }

    /// <summary>The application expects to run inside a terminal emulator.</summary>
    public bool Terminal { get; }

    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<string> OnlyShowIn { get; }
    public IReadOnlyList<string> NotShowIn { get; }

    /// <summary>True when this entry describes a launchable application that a menu should show.</summary>
    public bool IsVisibleApplication =>
        string.Equals(Type, "Application", StringComparison.Ordinal)
        && !NoDisplay
        && !Hidden
        && !string.IsNullOrWhiteSpace(Exec)
        && !string.IsNullOrWhiteSpace(Name);

    /// <summary>Reads <paramref name="path"/>. Returns null when the file cannot be read or has no
    /// <c>[Desktop Entry]</c> group; the caller logs, since only it knows whether that is worth
    /// reporting.</summary>
    public static DesktopEntry Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool inEntryGroup = false;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                // A second group starts; everything after [Desktop Entry] is out of scope.
                if (inEntryGroup)
                    break;

                inEntryGroup = string.Equals(line, EntryGroup, StringComparison.Ordinal);
                continue;
            }

            if (!inEntryGroup)
                continue;

            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            string key = line[..separator].TrimEnd();
            string value = line[(separator + 1)..].TrimStart();
            values.TryAdd(key, value);
        }

        return values.Count > 0 ? new DesktopEntry(path, values) : null;
    }

    /// <summary>
    /// Turns <see cref="Exec"/> into a program plus arguments. Field codes are dropped rather than
    /// expanded: they stand for files, URLs and menu metadata that a deck button never supplies, and
    /// the spec allows an empty expansion for all of them.
    /// </summary>
    public bool TryBuildCommandLine(out string fileName, out List<string> arguments)
    {
        fileName = null;
        arguments = [];

        List<string> tokens = SplitExec(Exec);
        if (tokens.Count == 0)
            return false;

        fileName = tokens[0];
        for (int i = 1; i < tokens.Count; i++)
        {
            string token = StripFieldCodes(tokens[i]);
            if (!string.IsNullOrEmpty(token))
                arguments.Add(token);
        }

        return !string.IsNullOrWhiteSpace(fileName);
    }

    /// <summary>
    /// Splits an <c>Exec</c> value into arguments. Per the spec a value is split on spaces except
    /// inside double quotes, where '\' escapes the following character.
    /// </summary>
    private static List<string> SplitExec(string exec)
    {
        List<string> tokens = [];
        if (string.IsNullOrWhiteSpace(exec))
            return tokens;

        StringBuilder current = new();
        bool quoted = false;
        bool any = false;

        for (int i = 0; i < exec.Length; i++)
        {
            char c = exec[i];

            if (quoted && (c == '\\') && (i + 1 < exec.Length))
            {
                current.Append(exec[++i]);
                any = true;
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
                any = true;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (any)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    any = false;
                }

                continue;
            }

            current.Append(c);
            any = true;
        }

        if (any)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>Removes the <c>%f %F %u %U %i %c %k</c> field codes and the deprecated
    /// <c>%d %D %n %N %v %m</c>. A literal percent sign is written <c>%%</c>.</summary>
    private static string StripFieldCodes(string token)
    {
        if (string.IsNullOrEmpty(token) || !token.Contains('%'))
            return token;

        StringBuilder result = new(token.Length);
        for (int i = 0; i < token.Length; i++)
        {
            if ((token[i] != '%') || (i + 1 >= token.Length))
            {
                result.Append(token[i]);
                continue;
            }

            char code = token[++i];
            if (code == '%')
                result.Append('%');

            // Every other code expands to nothing here.
        }

        return result.ToString();
    }

    /// <summary>
    /// Reads a key, preferring the current UI language: <c>Name[de_DE]</c>, then <c>Name[de]</c>,
    /// then the unsuffixed <c>Name</c>. Application names come from the system, so showing them the
    /// way the user's desktop menu does is the expected behaviour.
    /// </summary>
    private static string LocalizedValue(Dictionary<string, string> values, string key)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;

        if (!string.IsNullOrEmpty(culture.Name))
        {
            string full = culture.Name.Replace('-', '_');
            if (values.TryGetValue($"{key}[{full}]", out string localized))
                return localized;
        }

        string language = culture.TwoLetterISOLanguageName;
        if (!string.IsNullOrEmpty(language) && values.TryGetValue($"{key}[{language}]", out string byLanguage))
            return byLanguage;

        return Value(values, key);
    }

    private static string Value(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool Flag(Dictionary<string, string> values, string key)
        => string.Equals(Value(values, key), "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> List(Dictionary<string, string> values, string key)
    {
        string value = Value(values, key);
        if (value == null)
            return [];

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}