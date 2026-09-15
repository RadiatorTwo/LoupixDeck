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
