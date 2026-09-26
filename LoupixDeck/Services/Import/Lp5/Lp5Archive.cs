using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// The contents of a Loupedeck <c>.lp5</c> profile, read once into memory. An <c>.lp5</c> is a ZIP
/// holding <c>ApplicationInfo.json</c>, <c>ProfileInfo.json</c>, pre-rendered key images under
/// <c>ActionImages/</c> and icon descriptions under <c>ActionIcons/*.ict</c>. The archive is closed
/// again before <see cref="Open"/> returns, so one instance can feed several conversion passes.
/// </summary>
public sealed class Lp5Archive
{
    private const string ImagesFolder = "ActionImages/";
    private const string IconsFolder = "ActionIcons/";

    private readonly Dictionary<string, byte[]> _images;
    private readonly Dictionary<string, byte[]> _icons;

    private Lp5Archive(string fileName, JObject application, JObject profile,
        Dictionary<string, byte[]> images, Dictionary<string, byte[]> icons)
    {
        FileName = fileName;
        Application = application;
        Profile = profile;
        LayoutMode = Lp5Json.Arr(Lp5Json.Obj(profile, "layout"), "layoutModes").FirstOrDefault() as JObject
                     ?? new JObject();
        _images = images;
        _icons = icons;
    }

    public string FileName { get; }

    internal JObject Application { get; }

    internal JObject Profile { get; }

    /// <summary>The first layout mode, which carries the workspaces and pages.</summary>
    internal JObject LayoutMode { get; }

    public string ProfileName =>
        Lp5Json.Str(Profile, "displayName") ?? Lp5Json.Str(Application, "displayName") ??
        Path.GetFileNameWithoutExtension(FileName);

    public string ApplicationName => Lp5Json.Str(Application, "displayName");

    /// <summary>Process (Windows) or bundle (macOS) name the profile was bound to, if any.</summary>
    public string ProcessName => Lp5Json.Str(Application, "processOrBundleName");

    public string DeviceType => Lp5Json.Str(Application, "deviceType");

    /// <summary>Reads an <c>.lp5</c> file.</summary>
    /// <exception cref="InvalidDataException">The file is not a ZIP or lacks the profile JSON.</exception>
    public static Lp5Archive Open(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);

        JObject application = ReadJson(zip, "ApplicationInfo.json") ?? new JObject();
        JObject profile = ReadJson(zip, "ProfileInfo.json")
                          ?? throw new InvalidDataException("ProfileInfo.json is missing.");

        Dictionary<string, byte[]> images = new(StringComparer.Ordinal);
        Dictionary<string, byte[]> icons = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith(ImagesFolder, StringComparison.Ordinal) &&
                name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                // The first entry wins, as several names can decode to the same action.
                images.TryAdd(DecodedStem(name), ReadBytes(entry));
            }
            else if (name.StartsWith(IconsFolder, StringComparison.Ordinal) &&
                     name.EndsWith(".ict", StringComparison.OrdinalIgnoreCase))
            {
                icons[DecodedStem(name)] = ReadBytes(entry);
            }
        }

        return new Lp5Archive(Path.GetFileName(path), application, profile, images, icons);
    }

    /// <summary>
    /// The pre-rendered PNG of an action, preferring the exact name, then the first state of a
    /// multi-state action, then any other state.
    /// </summary>
    internal byte[] FindImage(string actionRef)
    {
        if (string.IsNullOrEmpty(actionRef)) return null;
        if (_images.TryGetValue(actionRef, out byte[] exact)) return exact;
        if (_images.TryGetValue(actionRef + "___state0000", out byte[] state0)) return state0;

        string prefix = actionRef + "___";
        return _images.FirstOrDefault(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Value;
    }

    /// <summary>The parsed <c>.ict</c> icon description of an action, or null.</summary>
    internal JObject FindIcon(string actionRef)
    {
        if (string.IsNullOrEmpty(actionRef) || !_icons.TryGetValue(actionRef, out byte[] data)) return null;

        try
        {
            return Parse(data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DecodedStem(string entryName)
    {
        string file = entryName[(entryName.LastIndexOf('/') + 1)..];
        int dot = file.LastIndexOf('.');
        return Uri.UnescapeDataString(dot > 0 ? file[..dot] : file);
    }

    private static JObject ReadJson(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry entry = zip.GetEntry(entryName);
        if (entry == null) return null;

        try
        {
            return Parse(ReadBytes(entry));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{entryName} is not valid JSON.", ex);
        }
    }

    private static JObject Parse(byte[] data)
    {
        // Loupedeck writes UTF-8 with a byte order mark; the reader strips it.
        using StreamReader reader = new(new MemoryStream(data), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JToken.Parse(reader.ReadToEnd()) as JObject;
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
