using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// A user-created folder inside a <see cref="Workspace"/> (issue #249). A folder owns its own
/// touch layout and may contain further folders, so the tree nests without a depth limit. The
/// tree is the only place nesting is defined; a button that opens a folder is just a link to
/// its <see cref="Id"/>.
/// </summary>
public sealed partial class CustomFolder : ObservableObject
{
    public CustomFolder()
    {
        // Assigned through the generated setter, not inline, so the collection is a real instance
        // even when JSON replaces it (see the collection-init gotcha in Workspace's constructor).
        Children = new();
    }

    /// <summary>Stable identity used by the open-folder command and by companion mirrors.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-assigned folder name, shown in the folder panel and the breadcrumbs.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>Sub-folders in display order.</summary>
    [ObservableProperty]
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public partial ObservableCollection<CustomFolder> Children { get; set; }

    public bool ShouldSerializeChildren() => Children is { Count: > 0 };

    /// <summary>
    /// The folder's touch layout. Null until the folder is first opened, so an empty folder costs
    /// nothing in the file and a companion mirror can be created without knowing the key count.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public TouchButtonPage Layout { get; set; }

    /// <summary>
    /// The folder's pages after the first (issue #603); <see cref="Layout"/> stays page 1, so a file
    /// written before folders had pages loads unchanged. Omitted from the file while empty. Null
    /// entries a hand-edited file may hold are dropped on load. Replaced as a whole, never changed
    /// in place, so the device read thread can read it without a lock.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<TouchButtonPage> ExtraPages
    {
        get;
        set => field = value?.Where(static p => p != null).ToList() ?? [];
    } = [];

    public bool ShouldSerializeExtraPages() => ExtraPages is { Count: > 0 };

    /// <summary>
    /// The page shown while this folder is open, 0 being <see cref="Layout"/>. Runtime only: a folder
    /// opened anew starts on its first page, and one returned to from a sub-folder keeps its page.
    /// </summary>
    [JsonIgnore]
    public int CurrentPageIndex { get; set; }

    /// <summary>Number of pages; a folder without a layout yet counts as one empty page.</summary>
    [JsonIgnore]
    public int PageCount => Layout == null ? 1 : 1 + ExtraPages.Count;

    /// <summary>The layout of the current page, or null while the folder has no layout yet.</summary>
    [JsonIgnore]
    public TouchButtonPage CurrentLayout => PageAt(CurrentPageIndex) ?? Layout;

    /// <summary>The layout of page <paramref name="index"/> (0 = <see cref="Layout"/>), or null when out of range.</summary>
    public TouchButtonPage PageAt(int index)
    {
        if (index == 0) return Layout;
        // Read once: page edits replace the list instead of changing it, so the device read
        // thread never indexes a list that shrinks under it.
        List<TouchButtonPage> extra = ExtraPages;
        if (Layout == null || index < 1 || index > extra.Count) return null;
        return extra[index - 1];
    }

    /// <summary>Every existing page layout of this folder, first page first.</summary>
    public IEnumerable<TouchButtonPage> Pages()
    {
        if (Layout == null) yield break;
        yield return Layout;
        foreach (TouchButtonPage page in ExtraPages)
            yield return page;
    }

    /// <summary>This folder followed by all its descendants, depth first.</summary>
    public IEnumerable<CustomFolder> SelfAndDescendants()
    {
        yield return this;
        foreach (CustomFolder child in Children ?? [])
        {
            if (child == null) continue;
            foreach (CustomFolder nested in child.SelfAndDescendants())
                yield return nested;
        }
    }
}
