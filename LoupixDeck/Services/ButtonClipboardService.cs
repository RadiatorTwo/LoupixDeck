using LoupixDeck.Models;
using LoupixDeck.Services.Folders;
using LoupixDeck.Services.Portable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

/// <summary>
/// The kind of element a clipboard snapshot came from. Paste is only allowed onto the same
/// kind (issue #166 compatibility rules) — the payloads are structurally different and cross
/// pasting would misplace or drop content.
/// </summary>
public enum ButtonKind
{
    Touch,
    Simple,
    Rotary,
    SideDisplay,

    /// <summary>A custom folder with its subtree, copied from the folder panel (issue #535).</summary>
    Folder,

    /// <summary>A whole touch page, copied from the page name's context menu (issue #621).</summary>
    TouchPage,

    /// <summary>A whole rotary page of a device that pages all its dials together (issue #621).</summary>
    RotaryPage,

    /// <summary>A whole rotary page of one dial column on a side-strip device (issue #621).</summary>
    SideRotaryPage,

    /// <summary>The three wallpaper slots of a touch page, without its keys (issue #663).</summary>
    PageBackground,

    /// <summary>A Live S dial label (<see cref="RotaryButton.DialLabel"/>), issue #676.</summary>
    DialLabel
}

/// <summary>
/// In-app clipboard for copy/paste of buttons and side displays (issue #166). Holds a JSON
/// <b>snapshot</b> of the copied element (independent of the source object, so it survives page /
/// device switches and later source edits), plus the source kind for compatibility checks.
/// Registered as a root singleton and forwarded into every device provider, so copy/paste works
/// across pages and devices. The serialize/apply logic lives in <see cref="ButtonSnapshot"/>.
/// </summary>
public interface IButtonClipboardService
{
    bool HasData { get; }
    ButtonKind? CurrentKind { get; }

    /// <summary>
    /// Folder snapshots (<see cref="FolderSnapshot"/>) that travel with the copied button: the
    /// folders it opens, so a paste into a workspace without them can recreate them (issue #535).
    /// Empty for anything but a touch button that opens a folder.
    /// </summary>
    IReadOnlyList<string> LinkedFolders { get; }

    /// <summary>The folder snapshot when <see cref="CurrentKind"/> is <see cref="ButtonKind.Folder"/>, otherwise null.</summary>
    string FolderJson { get; }

    /// <summary>Everything a paste brings: the copied element and the <see cref="LinkedFolders"/>. Empty when nothing is copied.</summary>
    IReadOnlyList<string> Snapshots { get; }

    /// <summary>Raised when the clipboard content changes (set/clear).</summary>
    event Action Changed;

    /// <summary>
    /// Take a deep snapshot of <paramref name="source"/> as the given kind, together with the
    /// snapshots of the folders it opens.
    /// </summary>
    void Copy(LoupedeckButton source, ButtonKind kind, IReadOnlyList<string> linkedFolders = null);

    /// <summary>Take a deep snapshot of <paramref name="folder"/> and its sub-folders.</summary>
    void CopyFolder(CustomFolder folder);

    /// <summary>
    /// Take a deep snapshot of a whole <paramref name="page"/> as the given page kind, together with
    /// the snapshots of the folders its keys open (issue #621).
    /// </summary>
    void CopyPage(ButtonPageBase page, ButtonKind kind, IReadOnlyList<string> linkedFolders = null);

    /// <summary>
    /// The page snapshot when <see cref="CurrentKind"/> is one of the page kinds, otherwise null.
    /// <paramref name="idMap"/> rewrites ids in it, e.g. folder links that now point at freshly
    /// pasted copies.
    /// </summary>
    string PageJson(IReadOnlyDictionary<Guid, Guid> idMap = null);

    /// <summary>
    /// Take a snapshot of the background of <paramref name="page"/>: its main and side wallpaper
    /// slots, with image, clip, generated fill and their placement (issue #663). The keys stay out.
    /// </summary>
    void CopyPageBackground(TouchButtonPage page);

    /// <summary>
    /// Overwrite the wallpaper slots of <paramref name="target"/> with the copied background.
    /// Does nothing unless <see cref="CurrentKind"/> is <see cref="ButtonKind.PageBackground"/>.
    /// </summary>
    void PasteBackgroundInto(TouchButtonPage target);

    /// <summary>
    /// Old → new ids of the <see cref="LinkedFolders"/> already pasted into the workspace, so
    /// pasting the same button there again links the copy made the first time instead of making
    /// another one. Empty until <see cref="RememberPastedFolders"/>; forgotten on the next copy.
    /// </summary>
    IReadOnlyDictionary<Guid, Guid> PastedFolders(Guid workspaceId);

    void RememberPastedFolders(Guid workspaceId, IReadOnlyDictionary<Guid, Guid> map);

    /// <summary>True when a snapshot exists and it can be pasted onto a target of this kind.</summary>
    bool CanPasteInto(ButtonKind targetKind);

    /// <summary>Apply the current snapshot onto <paramref name="target"/> in place. Caller must
    /// have checked <see cref="CanPasteInto"/>. <paramref name="idMap"/> rewrites ids in the
    /// snapshot first, e.g. folder links that now point at freshly pasted copies.</summary>
    void PasteInto(LoupedeckButton target, IReadOnlyDictionary<Guid, Guid> idMap = null);

    void Clear();

    /// <summary>True when a button/side-display currently holds no user configuration.</summary>
    bool IsEmpty(LoupedeckButton button);
}

public sealed class ButtonClipboardService : IButtonClipboardService
{
    private ButtonKind _kind;
    private string _json;
    private IReadOnlyList<string> _linkedFolders = [];
    private readonly Dictionary<Guid, IReadOnlyDictionary<Guid, Guid>> _pastedFolders = [];

    public bool HasData => _json != null;
    public ButtonKind? CurrentKind => _json != null ? _kind : null;
    public IReadOnlyList<string> LinkedFolders => _json != null ? _linkedFolders : [];
    public string FolderJson => _json != null && _kind == ButtonKind.Folder ? _json : null;
    public IReadOnlyList<string> Snapshots => _json != null ? [_json, .. _linkedFolders] : [];

    public event Action Changed;

    public void Copy(LoupedeckButton source, ButtonKind kind, IReadOnlyList<string> linkedFolders = null)
    {
        if (source == null) return;
        _kind = kind;
        _json = ButtonSnapshot.Capture(source);
        _linkedFolders = linkedFolders?.Where(static f => f != null).ToList() ?? [];
        _pastedFolders.Clear();
        Changed?.Invoke();
    }

    public void CopyFolder(CustomFolder folder)
    {
        if (folder == null) return;
        _kind = ButtonKind.Folder;
        _json = FolderSnapshot.Capture(folder);
        _linkedFolders = [];
        _pastedFolders.Clear();
        Changed?.Invoke();
    }

    public void CopyPage(ButtonPageBase page, ButtonKind kind, IReadOnlyList<string> linkedFolders = null)
    {
        if (page == null || !IsPageKind(kind)) return;
        _kind = kind;
        _json = JObject.FromObject(page, ButtonSnapshot.Serializer).ToString(Formatting.None);
        _linkedFolders = linkedFolders?.Where(static f => f != null).ToList() ?? [];
        _pastedFolders.Clear();
        Changed?.Invoke();
    }

    public string PageJson(IReadOnlyDictionary<Guid, Guid> idMap = null)
        => _json != null && IsPageKind(_kind) ? WithIds(_json, idMap) : null;

    public void CopyPageBackground(TouchButtonPage page)
    {
        if (page == null) return;
        _kind = ButtonKind.PageBackground;
        _json = new JObject
        {
            [nameof(TouchButtonPage.MainWallpaper)] = SlotToken(page.MainWallpaper),
            [nameof(TouchButtonPage.LeftWallpaper)] = SlotToken(page.LeftWallpaper),
            [nameof(TouchButtonPage.RightWallpaper)] = SlotToken(page.RightWallpaper)
        }.ToString(Formatting.None);
        _linkedFolders = [];
        _pastedFolders.Clear();
        Changed?.Invoke();
    }

    public void PasteBackgroundInto(TouchButtonPage target)
    {
        if (target == null || _json == null || _kind != ButtonKind.PageBackground) return;

        // Each slot gets its own copy, so editing the pasted background never touches the source.
        JObject snapshot = JObject.Parse(_json);
        target.MainWallpaper.CopyFrom(SlotFrom(snapshot, nameof(TouchButtonPage.MainWallpaper)));
        target.LeftWallpaper.CopyFrom(SlotFrom(snapshot, nameof(TouchButtonPage.LeftWallpaper)));
        target.RightWallpaper.CopyFrom(SlotFrom(snapshot, nameof(TouchButtonPage.RightWallpaper)));
    }

    private static JToken SlotToken(WallpaperSlot slot)
        => JToken.FromObject(slot ?? new WallpaperSlot(), ButtonSnapshot.Serializer);

    // A missing slot clears the target slot rather than leaving its old content behind.
    private static WallpaperSlot SlotFrom(JObject snapshot, string name)
        => snapshot[name]?.ToObject<WallpaperSlot>(ButtonSnapshot.Serializer) ?? new WallpaperSlot();

    private static bool IsPageKind(ButtonKind kind)
        => kind is ButtonKind.TouchPage or ButtonKind.RotaryPage or ButtonKind.SideRotaryPage;

    public IReadOnlyDictionary<Guid, Guid> PastedFolders(Guid workspaceId)
        => _pastedFolders.TryGetValue(workspaceId, out IReadOnlyDictionary<Guid, Guid> map) ? map : new Dictionary<Guid, Guid>();

    public void RememberPastedFolders(Guid workspaceId, IReadOnlyDictionary<Guid, Guid> map)
    {
        if (map != null)
            _pastedFolders[workspaceId] = map;
    }

    public bool CanPasteInto(ButtonKind targetKind) => _json != null && _kind == targetKind;

    public void PasteInto(LoupedeckButton target, IReadOnlyDictionary<Guid, Guid> idMap = null)
    {
        if (_json == null || _kind is ButtonKind.Folder or ButtonKind.PageBackground || IsPageKind(_kind)) return;

        ButtonSnapshot.Apply(WithIds(_json, idMap), target);
    }

    private static string WithIds(string json, IReadOnlyDictionary<Guid, Guid> idMap)
    {
        if (idMap is not { Count: > 0 }) return json;

        JObject snapshot = JObject.Parse(json);
        PortableIdRemapper.SubstituteGuids(snapshot, idMap);
        return snapshot.ToString(Formatting.None);
    }

    public void Clear()
    {
        if (_json == null) return;
        _json = null;
        _linkedFolders = [];
        _pastedFolders.Clear();
        Changed?.Invoke();
    }

    public bool IsEmpty(LoupedeckButton button) => ButtonSnapshot.IsEmpty(button);
}
