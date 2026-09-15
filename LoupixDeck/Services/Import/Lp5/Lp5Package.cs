using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>One control of a Loupedeck dial page: the press and the rotate action (either may be null).</summary>
public sealed record Lp5Encoder(string PressAction, string RotateAction);

/// <summary>A Loupedeck touch page: one press action per key, index-aligned (null = empty key).</summary>
public sealed record Lp5TouchPage(string Name, IReadOnlyList<string> PressActions);

/// <summary>A Loupedeck dial page: six encoders, left column first.</summary>
public sealed record Lp5EncoderPage(string Name, IReadOnlyList<Lp5Encoder> Encoders);

/// <summary>A Loupedeck workspace with the pages it lists, in their listed order.</summary>
public sealed record Lp5Workspace(string Key, string Name, IReadOnlyList<Lp5TouchPage> TouchPages,
    IReadOnlyList<Lp5EncoderPage> EncoderPages);

/// <summary>Icon bytes stored for an action. <see cref="IsSvg"/> marks a vector glyph that must be rasterized.</summary>
public sealed record Lp5Icon(byte[] Bytes, bool IsSvg, bool IsPreRendered);

/// <summary>
/// The parts of a Loupedeck (Logi Options+) <c>.lp5</c> profile export that an import needs, read once
/// into memory. A <c>.lp5</c> is a ZIP: <c>ProfileInfo.json</c> holds the layout and the action
/// definitions, <c>ApplicationInfo.json</c> the linked application, and the key images live either in
/// <c>ActionImages/&lt;action&gt;.png</c> (fully rendered keys) or in <c>ActionIcons/&lt;action&gt;.ict</c>
/// (a JSON wrapper around a base64 PNG or SVG glyph, used by newer exports).
/// </summary>
public sealed class Lp5Package
{
    private const string ProfileInfoEntry = "ProfileInfo.json";
    private const string ApplicationInfoEntry = "ApplicationInfo.json";

    private readonly Dictionary<string, Lp5Icon> _icons = new(StringComparer.Ordinal);

    private Lp5Package()
    {
    }

    public string Name { get; private init; } = string.Empty;
    public string DeviceType { get; private init; } = string.Empty;

    /// <summary>Process name of the linked application without extension, or empty.</summary>
    public string ProcessName { get; private init; } = string.Empty;

    /// <summary>Display name of the linked application, or empty.</summary>
    public string ApplicationName { get; private init; } = string.Empty;

    public IReadOnlyList<Lp5Workspace> Workspaces { get; private set; } = [];

    /// <summary>Key of the home workspace; may not match any workspace.</summary>
    public string HomeWorkspaceKey { get; private init; } = string.Empty;

    /// <summary>Profile actions (keyboard shortcuts, text…) by their full action name.</summary>
    public IReadOnlyDictionary<string, JObject> ProfileActions { get; private set; }

    /// <summary>Multi-step macros by their id (the part after <c>@Macro___</c>).</summary>
    public IReadOnlyDictionary<string, JObject> MacroCommands { get; private set; }

    /// <summary>Dial macros (left / right / reset action lists) by their id.</summary>
    public IReadOnlyDictionary<string, JObject> MacroAdjustments { get; private set; }

    /// <summary>Number of controls with an Fn-layer action, which LoupixDeck has no equivalent for.</summary>
    public int FnActionCount { get; private set; }

    /// <summary>The icon stored for <paramref name="action"/>, or null.</summary>
    public Lp5Icon GetIcon(string action) =>
        !string.IsNullOrEmpty(action) && _icons.TryGetValue(action, out Lp5Icon icon) ? icon : null;

    /// <summary>
    /// Reads <paramref name="path"/>. Throws <see cref="InvalidDataException"/> when the file is not a
    /// Loupedeck profile export.
    /// </summary>
    public static Lp5Package Load(string path)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("The file is not a Loupedeck profile (.lp5).");
        }

        using (zip)
        {
            JObject profile = ReadJson(zip, ProfileInfoEntry)
                              ?? throw new InvalidDataException("The file is not a Loupedeck profile (.lp5): ProfileInfo.json is missing.");
            JObject application = ReadJson(zip, ApplicationInfoEntry);

            JObject mode = profile["layout"]?["layoutModes"]?.OfType<JObject>().FirstOrDefault();

            Lp5Package package = new()
            {
                Name = Text(profile["displayName"]) is { Length: > 0 } name ? name : "Loupedeck",
                DeviceType = Text(profile["deviceType"]),
                ProcessName = FirstNonEmpty(Text(application?["processOrBundleName"]), Text(profile["applicationName"])),
                ApplicationName = FirstNonEmpty(Text(application?["displayName"]), Text(profile["applicationName"])),
                HomeWorkspaceKey = Text(mode?["homeWorkspaceName"])
            };

            package.ProfileActions = IndexByName(profile["profileActions"], profile["profileCommands"], profile["profileAdjustments"]);
            package.MacroCommands = IndexByName(profile["macroCommands"]);
            package.MacroAdjustments = IndexByName(profile["macroAdjustments"]);
            package.Workspaces = package.ReadWorkspaces(mode);
            package.ReadIcons(zip);

            return package;
        }
    }

    private List<Lp5Workspace> ReadWorkspaces(JObject mode)
    {
        Dictionary<string, Lp5TouchPage> touchPages = new(StringComparer.OrdinalIgnoreCase);
        List<Lp5TouchPage> touchOrder = [];
        foreach (JObject page in mode?["touchPages"]?.OfType<JObject>() ?? [])
        {
            List<string> presses = [];
            foreach (JToken control in page["controls"] as JArray ?? [])
            {
                presses.Add(NullIfEmpty(Text(control?["pressAction"])));
                if (!string.IsNullOrEmpty(Text(control?["fnPressAction"])))
                    FnActionCount++;
            }

            Lp5TouchPage touch = new(Text(page["displayName"]), presses);
            touchPages[Text(page["name"])] = touch;
            touchOrder.Add(touch);
        }

        Dictionary<string, Lp5EncoderPage> encoderPages = new(StringComparer.OrdinalIgnoreCase);
        List<Lp5EncoderPage> encoderOrder = [];
        foreach (JObject page in mode?["encoderPages"]?.OfType<JObject>() ?? [])
        {
            List<Lp5Encoder> encoders = [];
            foreach (JToken control in page["controls"] as JArray ?? [])
            {
                encoders.Add(new Lp5Encoder(NullIfEmpty(Text(control?["pressAction"])),
                    NullIfEmpty(Text(control?["rotateAction"]))));
                if (!string.IsNullOrEmpty(Text(control?["fnPressAction"])) ||
                    !string.IsNullOrEmpty(Text(control?["fnRotateAction"])))
                    FnActionCount++;
            }

            Lp5EncoderPage encoder = new(Text(page["displayName"]), encoders);
            encoderPages[Text(page["name"])] = encoder;
            encoderOrder.Add(encoder);
        }

        List<Lp5Workspace> workspaces = [];
        foreach (JObject workspace in mode?["workspaces"]?.OfType<JObject>() ?? [])
        {
            List<Lp5TouchPage> touches = (workspace["touchPageNames"] as JArray ?? [])
                .Select(t => touchPages.GetValueOrDefault(Text(t)))
                .Where(p => p != null)
                .ToList();
            List<Lp5EncoderPage> encoders = (workspace["encoderPageNames"] as JArray ?? [])
                .Select(t => encoderPages.GetValueOrDefault(Text(t)))
                .Where(p => p != null)
                .ToList();

            workspaces.Add(new Lp5Workspace(Text(workspace["name"]), Text(workspace["displayName"]), touches, encoders));
        }

        // Older exports have no workspace list: everything belongs to one implicit workspace.
        if (workspaces.Count == 0 && (touchOrder.Count > 0 || encoderOrder.Count > 0))
            workspaces.Add(new Lp5Workspace(string.Empty, string.Empty, touchOrder, encoderOrder));

        return workspaces;
    }

    private void ReadIcons(ZipArchive zip)
    {
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            try
            {
                if (TryStripFolder(entry.FullName, "ActionImages/", ".png", out string imageAction))
                {
                    _icons[imageAction] = new Lp5Icon(ReadBytes(entry), IsSvg: false, IsPreRendered: true);
                }
                else if (TryStripFolder(entry.FullName, "ActionIcons/", ".ict", out string iconAction) &&
                         !_icons.ContainsKey(iconAction) &&
                         ReadIct(entry) is { } icon)
                {
                    _icons[iconAction] = icon;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or Newtonsoft.Json.JsonException)
            {
                Console.WriteLine($"[lp5] Skipped unreadable icon '{entry.FullName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// An <c>.ict</c> wraps the glyph as base64 in <c>items[].image</c>. A large PNG is the fully
    /// rendered key (text included); a small one or an SVG is only the glyph.
    /// </summary>
    private static Lp5Icon ReadIct(ZipArchiveEntry entry)
    {
        using StreamReader reader = new(entry.Open());
        JObject ict = JObject.Parse(reader.ReadToEnd());

        string base64 = (ict["items"] as JArray)?
            .OfType<JObject>()
            .Select(item => Text(item["image"]))
            .FirstOrDefault(s => s.Length > 0);
        if (string.IsNullOrEmpty(base64))
            return null;

        byte[] bytes = Convert.FromBase64String(base64);
        bool isPng = bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        return new Lp5Icon(bytes, IsSvg: !isPng, IsPreRendered: isPng && bytes.Length > 4000);
    }

    private static bool TryStripFolder(string fullName, string folder, string extension, out string action)
    {
        action = null;
        if (!fullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ||
            !fullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return false;

        action = fullName[folder.Length..^extension.Length];
        return action.Length > 0 && !action.Contains('/');
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static JObject ReadJson(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name);
        if (entry == null)
            return null;

        // StreamReader strips the UTF-8 BOM Loupedeck writes.
        using StreamReader reader = new(entry.Open());
        try
        {
            return JObject.Parse(reader.ReadToEnd());
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new InvalidDataException($"The Loupedeck profile is damaged: {name} cannot be read ({ex.Message}).");
        }
    }

    private static Dictionary<string, JObject> IndexByName(params JToken[] arrays)
    {
        Dictionary<string, JObject> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (JToken array in arrays)
        {
            foreach (JObject item in (array as JArray)?.OfType<JObject>() ?? [])
            {
                string name = Text(item["name"]);
                if (name.Length > 0)
                    index[name] = item;
            }
        }

        return index;
    }

    internal static string Text(JToken token) =>
        token is JValue { Value: not null } value ? value.ToString() ?? string.Empty : string.Empty;

    private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;
}
