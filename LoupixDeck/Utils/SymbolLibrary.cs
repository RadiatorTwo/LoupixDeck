using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Avalonia.Platform;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>The bundled icon fonts a symbol can come from.</summary>
public enum SymbolFontLibrary
{
    /// <summary>Material Design Icons. Ids of this library carry no prefix.</summary>
    Mdi,

    /// <summary>Material Design Light. Ids carry the <see cref="SymbolLibrary.LightIdPrefix"/>.</summary>
    MdiLight
}

/// <summary>
/// One selectable icon from one of the bundled icon webfonts.
/// </summary>
public sealed record SymbolDefinition(
    string Id, string DisplayName, string Category, int Codepoint,
    SymbolFontLibrary Library = SymbolFontLibrary.Mdi)
{
    /// <summary>The UTF-16 string that renders this glyph in its library's font.</summary>
    public string Glyph => char.ConvertFromUtf32(Codepoint);

    /// <summary>avares font URI of the library, for views that render the glyph.</summary>
    public string FontUri => SymbolLibrary.GetFontUri(Library);

    /// <summary>Upstream tags (the icon groups on pictogrammers.com); empty for curated entries.</summary>
    public ImmutableArray<string> Tags { get; init; } = [];

    /// <summary>Upstream alternative names, used by the picker search.</summary>
    public ImmutableArray<string> Aliases { get; init; } = [];
}

/// <summary>
/// Registry of the symbols a <c>SymbolLayer</c> can show. The curated list <see cref="All"/> holds
/// the most common Loupedeck symbols and is the picker's default. The full sets of the bundled
/// Material Design Icons (Pictogrammers Free License) and Material Design Light (SIL OFL 1.1) fonts
/// come from the generated catalogs next to the fonts, which also record each font's version
/// (see <c>tools/UpdateMdiFont</c>).
/// </summary>
public static class SymbolLibrary
{
    /// <summary>
    /// avares URI of the bundled MDI webfont. Also usable directly as an
    /// Avalonia <c>FontFamily</c> (see the <c>MdiFont</c> resource in App.axaml).
    /// </summary>
    public const string FontUri =
        "avares://LoupixDeck/Assets/Fonts/materialdesignicons-webfont.ttf#Material Design Icons";

    /// <summary>avares URI of the bundled Material Design Light webfont.</summary>
    public const string LightFontUri =
        "avares://LoupixDeck/Assets/Fonts/materialdesignicons-light-webfont.ttf#Material Design Icons Light";

    /// <summary>
    /// Id prefix of Material Design Light symbols. Almost every Light name also exists in MDI, so
    /// the prefix keeps the stored ids apart; MDI ids stay unprefixed as they always were.
    /// </summary>
    public const string LightIdPrefix = "mdil:";

    private const string AssetBase = "avares://LoupixDeck/Assets/Fonts/";

    private static readonly Lock Sync = new();
    private static readonly Dictionary<SymbolFontLibrary, SKTypeface> Typefaces = [];
    private static readonly HashSet<SymbolFontLibrary> FailedTypefaces = [];

    private static readonly Lazy<SymbolCatalog> MdiCatalog =
        new(() => SymbolCatalog.Load(SymbolFontLibrary.Mdi, "mdi-catalog.json", string.Empty));

    private static readonly Lazy<SymbolCatalog> LightCatalog =
        new(() => SymbolCatalog.Load(SymbolFontLibrary.MdiLight, "mdil-catalog.json", LightIdPrefix));

    public const string AllCategoriesKey = "All";

    /// <summary>Category of full-catalog icons that carry no upstream tag.</summary>
    public const string OtherCategoryKey = "Other";

    public static ImmutableArray<SymbolDefinition> All { get; } =
    [
        // Media
        new("play", "Play", "Media", 0xF040A),
        new("pause", "Pause", "Media", 0xF03E4),
        new("stop", "Stop", "Media", 0xF04DB),
        new("record", "Record", "Media", 0xF044A),
        new("skip-previous", "Previous", "Media", 0xF04AE),
        new("skip-next", "Next", "Media", 0xF04AD),
        new("rewind", "Rewind", "Media", 0xF045F),
        new("fast-forward", "Fast Forward", "Media", 0xF0211),
        new("repeat", "Repeat", "Media", 0xF0456),
        new("shuffle", "Shuffle", "Media", 0xF049D),
        new("eject", "Eject", "Media", 0xF01EA),
        new("movie-open-outline", "Movie", "Media", 0xF0FCF),
        // Audio
        new("volume-high", "Volume High", "Audio", 0xF057E),
        new("volume-medium", "Volume Medium", "Audio", 0xF0580),
        new("volume-low", "Volume Low", "Audio", 0xF057F),
        new("volume-off", "Volume Off", "Audio", 0xF0581),
        new("volume-mute", "Mute", "Audio", 0xF075F),
        new("microphone", "Microphone", "Audio", 0xF036C),
        new("microphone-off", "Mic Off", "Audio", 0xF036D),
        new("headphones", "Headphones", "Audio", 0xF02CB),
        new("speaker", "Speaker", "Audio", 0xF04C3),
        new("music", "Music", "Audio", 0xF075A),
        new("equalizer", "Equalizer", "Audio", 0xF0EA2),
        new("tune", "Tune", "Audio", 0xF062E),
        // Capture
        new("camera", "Camera", "Capture", 0xF0100),
        new("camera-off", "Camera Off", "Capture", 0xF05DF),
        new("video", "Video", "Capture", 0xF0567),
        new("video-off", "Video Off", "Capture", 0xF0568),
        new("webcam", "Webcam", "Capture", 0xF05A0),
        new("monitor", "Monitor", "Capture", 0xF0379),
        new("monitor-screenshot", "Screenshot", "Capture", 0xF0E51),
        new("broadcast", "Broadcast", "Capture", 0xF1720),
        new("television", "Television", "Capture", 0xF0502),
        new("cast", "Cast", "Capture", 0xF0118),
        // Lighting
        new("lightbulb", "Lightbulb", "Lighting", 0xF0335),
        new("lightbulb-on", "Lightbulb On", "Lighting", 0xF06E8),
        new("lightbulb-off", "Lightbulb Off", "Lighting", 0xF0E4F),
        new("white-balance-sunny", "Sun", "Lighting", 0xF05A8),
        new("weather-night", "Moon", "Lighting", 0xF0594),
        new("brightness-6", "Brightness", "Lighting", 0xF00DF),
        new("flash", "Flash", "Lighting", 0xF0241),
        new("flashlight", "Flashlight", "Lighting", 0xF0244),
        // System
        new("power", "Power", "System", 0xF0425),
        new("power-plug", "Power Plug", "System", 0xF06A5),
        new("cog", "Settings", "System", 0xF0493),
        new("restart", "Restart", "System", 0xF0709),
        new("sleep", "Sleep", "System", 0xF04B2),
        new("lock", "Lock", "System", 0xF033E),
        new("lock-open", "Unlock", "System", 0xF033F),
        new("folder", "Folder", "System", 0xF024B),
        new("folder-open", "Folder Open", "System", 0xF0770),
        new("file", "File", "System", 0xF0214),
        new("home", "Home", "System", 0xF02DC),
        new("web", "Web", "System", 0xF059F),
        new("magnify", "Search", "System", 0xF0349),
        new("delete", "Delete", "System", 0xF01B4),
        new("refresh", "Refresh", "System", 0xF0450),
        new("sync", "Sync", "System", 0xF04E6),
        new("download", "Download", "System", 0xF01DA),
        new("upload", "Upload", "System", 0xF0552),
        new("content-copy", "Copy", "System", 0xF018F),
        new("content-paste", "Paste", "System", 0xF0192),
        new("content-cut", "Cut", "System", 0xF0190),
        new("content-save", "Save", "System", 0xF0193),
        // Communication
        new("email", "Email", "Communication", 0xF01EE),
        new("message", "Message", "Communication", 0xF0361),
        new("chat", "Chat", "Communication", 0xF0B79),
        new("phone", "Phone", "Communication", 0xF03F2),
        new("bell", "Bell", "Communication", 0xF009A),
        new("bell-off", "Bell Off", "Communication", 0xF009B),
        new("send", "Send", "Communication", 0xF048A),
        new("account", "Account", "Communication", 0xF0004),
        // Navigation
        new("arrow-up", "Arrow Up", "Navigation", 0xF005D),
        new("arrow-down", "Arrow Down", "Navigation", 0xF0045),
        new("arrow-left", "Arrow Left", "Navigation", 0xF004D),
        new("arrow-right", "Arrow Right", "Navigation", 0xF0054),
        new("chevron-up", "Chevron Up", "Navigation", 0xF0143),
        new("chevron-down", "Chevron Down", "Navigation", 0xF0140),
        new("chevron-left", "Chevron Left", "Navigation", 0xF0141),
        new("chevron-right", "Chevron Right", "Navigation", 0xF0142),
        new("undo", "Undo", "Navigation", 0xF054C),
        new("redo", "Redo", "Navigation", 0xF044E),
        new("exit-to-app", "Exit", "Navigation", 0xF0206),
        new("menu", "Menu", "Navigation", 0xF035C),
        new("dots-horizontal", "More", "Navigation", 0xF01D8),
        // Symbols
        new("star", "Star", "Symbols", 0xF04CE),
        new("star-outline", "Star Outline", "Symbols", 0xF04D2),
        new("heart", "Heart", "Symbols", 0xF02D1),
        new("heart-outline", "Heart Outline", "Symbols", 0xF02D5),
        new("check", "Check", "Symbols", 0xF012C),
        new("close", "Close", "Symbols", 0xF0156),
        new("plus", "Plus", "Symbols", 0xF0415),
        new("minus", "Minus", "Symbols", 0xF0374),
        new("alert", "Alert", "Symbols", 0xF0026),
        new("alert-circle", "Alert Circle", "Symbols", 0xF0028),
        new("information", "Information", "Symbols", 0xF02FC),
        new("help-circle", "Help", "Symbols", 0xF02D7),
        new("eye", "Eye", "Symbols", 0xF0208),
        new("eye-off", "Eye Off", "Symbols", 0xF0209),
        new("flag", "Flag", "Symbols", 0xF023B),
        new("bookmark", "Bookmark", "Symbols", 0xF00C0),
        new("tag", "Tag", "Symbols", 0xF04F9),
        new("fire", "Fire", "Symbols", 0xF0238),
        new("rocket-launch", "Rocket", "Symbols", 0xF14DE),
        new("trophy", "Trophy", "Symbols", 0xF0538),
        new("gift", "Gift", "Symbols", 0xF0E44),
        new("thumb-up", "Thumb Up", "Symbols", 0xF0513),
        // Devices
        new("keyboard", "Keyboard", "Devices", 0xF030C),
        new("mouse", "Mouse", "Devices", 0xF037D),
        new("desktop-classic", "Desktop", "Devices", 0xF07C0),
        new("laptop", "Laptop", "Devices", 0xF0322),
        new("gamepad-variant", "Gamepad", "Devices", 0xF0297),
        new("wifi", "WiFi", "Devices", 0xF05A9),
        new("bluetooth", "Bluetooth", "Devices", 0xF00AF),
        new("usb", "USB", "Devices", 0xF0553),
        new("printer", "Printer", "Devices", 0xF042A),
        new("calendar", "Calendar", "Devices", 0xF00ED),
        new("clock", "Clock", "Devices", 0xF0954),
        new("image", "Image", "Devices", 0xF02E9),
        new("palette", "Palette", "Devices", 0xF03D8),
        new("pencil", "Pencil", "Devices", 0xF03EB),
        new("numeric-1-box", "Number 1", "Devices", 0xF03A4),
        new("numeric-2-box", "Number 2", "Devices", 0xF03A7),
        new("numeric-3-box", "Number 3", "Devices", 0xF03AA),
    ];

    private static readonly FrozenDictionary<string, SymbolDefinition> ById =
        All.ToFrozenDictionary(static s => s.Id, StringComparer.OrdinalIgnoreCase);

    // Several ids can share a code point (the same glyph offered under two names); the first
    // one listed wins, so the mapping is stable against later additions to the catalogue.
    private static readonly FrozenDictionary<string, SymbolDefinition> ByGlyph =
        All.GroupBy(static s => s.Glyph, StringComparer.Ordinal)
            .ToFrozenDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);

    /// <summary>Distinct category names, in first-seen order.</summary>
    public static ImmutableArray<string> Categories { get; } =
        All.Select(static s => s.Category).Distinct().ToImmutableArray();

    public static ImmutableArray<string> CategoriesWithAll { get; } = Categories.Insert(0, AllCategoriesKey);

    /// <summary>
    /// Looks up a symbol by its stable id (the value stored in <c>SymbolLayer.SymbolId</c>).
    /// A <see cref="LightIdPrefix"/> id resolves in the Material Design Light catalog. Any other id
    /// resolves in the curated list first, which keeps curated display names, and then in the full
    /// MDI catalog by name or alias. The full catalogs load on the first miss only.
    /// </summary>
    public static bool TryGet(string id, out SymbolDefinition definition)
    {
        definition = null;
        if (string.IsNullOrEmpty(id))
            return false;

        if (id.StartsWith(LightIdPrefix, StringComparison.OrdinalIgnoreCase))
            return LightCatalog.Value.TryGetByName(id[LightIdPrefix.Length..], out definition);

        return ById.TryGetValue(id, out definition) || MdiCatalog.Value.TryGetByName(id, out definition);
    }

    /// <summary>All non-deprecated icons of a library, sorted by name.</summary>
    public static ImmutableArray<SymbolDefinition> FullIcons(SymbolFontLibrary library) => Catalog(library).Icons;

    /// <summary>The upstream tags of a library, sorted, plus <see cref="OtherCategoryKey"/> when used.</summary>
    public static ImmutableArray<string> FullCategories(SymbolFontLibrary library) => Catalog(library).Categories;

    /// <summary>
    /// npm package version of the bundled font, as recorded in its catalog, or null when the
    /// catalog is missing. <c>tools/UpdateMdiFont/update-mdi-font.ps1</c> compares it with the
    /// latest release.
    /// </summary>
    public static string LibraryVersion(SymbolFontLibrary library) => Catalog(library).Version;

    public static string GetFontUri(SymbolFontLibrary library) =>
        library == SymbolFontLibrary.MdiLight ? LightFontUri : FontUri;

    private static SymbolCatalog Catalog(SymbolFontLibrary library) =>
        library == SymbolFontLibrary.MdiLight ? LightCatalog.Value : MdiCatalog.Value;

    /// <summary>
    /// Looks up a symbol by the glyph string itself, for callers that only have the rendered
    /// character — commands declare their picker icon that way (<c>CommandAttribute.Icon</c>), and
    /// putting one on a button needs the id a <c>SymbolLayer</c> stores. A glyph outside the curated
    /// subset simply has no id, which callers treat as "no symbol" rather than as an error.
    /// </summary>
    public static bool TryGetByGlyph(string glyph, out SymbolDefinition definition)
    {
        if (!string.IsNullOrEmpty(glyph))
            return ByGlyph.TryGetValue(glyph, out definition);

        definition = null;
        return false;
    }

    public static string GlyphString(int codepoint) => char.ConvertFromUtf32(codepoint);

    /// <summary>
    /// Lazily loads and caches the MDI <see cref="SKTypeface"/> from the bundled
    /// resource. Returns null if the font asset is missing or unreadable — callers
    /// should fall back to a placeholder render in that case.
    /// </summary>
    public static SKTypeface GetTypeface() => GetTypeface(SymbolFontLibrary.Mdi);

    /// <summary>
    /// Lazily loads and caches the <see cref="SKTypeface"/> of <paramref name="library"/>.
    /// Returns null if the font asset is missing or unreadable.
    /// </summary>
    public static SKTypeface GetTypeface(SymbolFontLibrary library)
    {
        lock (Sync)
        {
            if (Typefaces.TryGetValue(library, out SKTypeface cached)) return cached;
            if (FailedTypefaces.Contains(library)) return null;

            SKTypeface typeface;
            try
            {
                string uri = GetFontUri(library);
                using Stream stream = AssetLoader.Open(new Uri(uri[..uri.IndexOf('#')]));
                using SKData data = SKData.Create(stream);
                typeface = SKTypeface.FromData(data);
            }
            catch
            {
                typeface = null;
            }

            if (typeface == null)
                FailedTypefaces.Add(library);
            else
                Typefaces[library] = typeface;

            return typeface;
        }
    }

    /// <summary>
    /// The full icon set of one library, parsed from its generated catalog asset. A missing or
    /// broken catalog yields an empty set, so the curated symbols keep working.
    /// </summary>
    private sealed class SymbolCatalog
    {
        private readonly FrozenDictionary<string, SymbolDefinition> _byName;

        private SymbolCatalog(string version, ImmutableArray<SymbolDefinition> icons,
            FrozenDictionary<string, SymbolDefinition> byName)
        {
            Version = version;
            Icons = icons;
            _byName = byName;
            Categories = icons
                .SelectMany(static s => s.Tags.IsEmpty ? [OtherCategoryKey] : s.Tags.AsEnumerable())
                .Distinct()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        public string Version { get; }

        public ImmutableArray<SymbolDefinition> Icons { get; }

        public ImmutableArray<string> Categories { get; }

        public bool TryGetByName(string name, out SymbolDefinition definition) =>
            _byName.TryGetValue(name, out definition);

        public static SymbolCatalog Load(SymbolFontLibrary library, string fileName, string idPrefix)
        {
            try
            {
                using Stream stream = AssetLoader.Open(new Uri(AssetBase + fileName));
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;

                ImmutableArray<SymbolDefinition>.Builder icons = ImmutableArray.CreateBuilder<SymbolDefinition>();
                Dictionary<string, SymbolDefinition> byName = new(StringComparer.OrdinalIgnoreCase);
                List<(string Alias, SymbolDefinition Definition)> aliases = [];

                foreach (JsonElement entry in root.GetProperty("icons").EnumerateArray())
                {
                    string name = entry[0].GetString();
                    ImmutableArray<string> tags = ReadStrings(entry[2]);
                    SymbolDefinition definition = new(
                        idPrefix + name,
                        ToDisplayName(name),
                        tags.IsEmpty ? OtherCategoryKey : tags[0],
                        int.Parse(entry[1].GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        library)
                    {
                        Tags = tags,
                        Aliases = ReadStrings(entry[3])
                    };

                    icons.Add(definition);
                    byName[name] = definition;
                    foreach (string alias in definition.Aliases)
                        aliases.Add((alias, definition));
                }

                // Real names win over aliases, so an alias never shadows another icon.
                foreach ((string alias, SymbolDefinition definition) in aliases)
                    byName.TryAdd(alias, definition);

                return new SymbolCatalog(
                    root.GetProperty("version").GetString(),
                    icons.ToImmutable(),
                    byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SymbolLibrary] Failed to load '{fileName}': {ex.Message}");
                return new SymbolCatalog(null, [], FrozenDictionary<string, SymbolDefinition>.Empty);
            }
        }

        private static ImmutableArray<string> ReadStrings(JsonElement array) =>
            [.. array.EnumerateArray().Select(static e => e.GetString())];

        /// <summary>"volume-high" -> "Volume High".</summary>
        private static string ToDisplayName(string name) =>
            string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
