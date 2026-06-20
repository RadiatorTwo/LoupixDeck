using Avalonia.Platform;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// One selectable icon from the bundled Material Design Icons webfont.
/// </summary>
public sealed record SymbolDefinition(string Id, string DisplayName, string Category, int Codepoint)
{
    /// <summary>The UTF-16 string that renders this glyph in the MDI font.</summary>
    public string Glyph => char.ConvertFromUtf32(Codepoint);
}

/// <summary>
/// Curated registry of the most common Loupedeck symbols, backed by the
/// Material Design Icons webfont (Pictogrammers, Apache-2.0). The font ships
/// thousands of glyphs; only this curated subset is offered in the picker.
/// </summary>
public static class SymbolLibrary
{
    /// <summary>
    /// avares URI of the bundled MDI webfont. Also usable directly as an
    /// Avalonia <c>FontFamily</c> (see the <c>MdiFont</c> resource in App.axaml).
    /// </summary>
    public const string FontUri =
        "avares://LoupixDeck/Assets/Fonts/materialdesignicons-webfont.ttf#Material Design Icons";

    private static readonly Uri FontAssetUri =
        new("avares://LoupixDeck/Assets/Fonts/materialdesignicons-webfont.ttf");

    private static readonly Lock Sync = new();
    private static SKTypeface _typeface;
    private static bool _typefaceLoadFailed;

    public static IReadOnlyList<SymbolDefinition> All { get; } =
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

    private static readonly Dictionary<string, SymbolDefinition> ById =
        All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Distinct category names, in first-seen order.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        All.Select(s => s.Category).Distinct().ToArray();

    /// <summary>Looks up a symbol by its stable id (the value stored in <c>SymbolLayer.SymbolId</c>).</summary>
    public static bool TryGet(string id, out SymbolDefinition definition)
    {
        if (!string.IsNullOrEmpty(id))
            return ById.TryGetValue(id, out definition);

        definition = null;
        return false;
    }

    public static string GlyphString(int codepoint) => char.ConvertFromUtf32(codepoint);

    /// <summary>
    /// Lazily loads and caches the MDI <see cref="SKTypeface"/> from the bundled
    /// resource. Returns null if the font asset is missing or unreadable — callers
    /// should fall back to a placeholder render in that case.
    /// </summary>
    public static SKTypeface GetTypeface()
    {
        if (_typeface != null) return _typeface;
        if (_typefaceLoadFailed) return null;

        lock (Sync)
        {
            if (_typeface != null) return _typeface;
            if (_typefaceLoadFailed) return null;

            try
            {
                using var stream = AssetLoader.Open(FontAssetUri);
                using var data = SKData.Create(stream);
                _typeface = SKTypeface.FromData(data);
            }
            catch
            {
                _typeface = null;
            }

            if (_typeface == null) _typefaceLoadFailed = true;
            return _typeface;
        }
    }
}
