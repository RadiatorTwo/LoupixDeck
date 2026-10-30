using LoupixDeck.Models;
using LoupixDeck.Services.Portable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Folders;

/// <summary>
/// Serialize/instantiate codec for copying a custom folder with its whole subtree (issue #535).
/// A snapshot is a JSON deep copy of the folder: name, touch layout and sub-folders. The automatic
/// Back tile is runtime-only (<see cref="TouchButton.IsFolderBackSlot"/> is not serialized), so it
/// never travels with the copy.
/// </summary>
public static class FolderSnapshot
{
    /// <summary>Deep-snapshot <paramref name="folder"/> and its sub-folders to JSON. Returns null for a null folder.</summary>
    public static string Capture(CustomFolder folder)
    {
        if (folder == null) return null;
        return JObject.FromObject(folder, ButtonSnapshot.Serializer).ToString(Formatting.None);
    }

    /// <summary>
    /// Builds a new folder tree from a snapshot. Every folder, state and layer gets a fresh id, and
    /// every reference inside the tree (a nested folder button, a state switch) follows to the copy,
    /// so the result can live next to the original. Links to folders outside the tree are left as
    /// they are; the caller decides whether they resolve in the target workspace.
    /// </summary>
    /// <param name="json">A snapshot made by <see cref="Capture"/>.</param>
    /// <param name="map">Old → new id of everything that was renumbered.</param>
    public static CustomFolder Instantiate(string json, out IReadOnlyDictionary<Guid, Guid> map)
    {
        map = new Dictionary<Guid, Guid>();
        if (string.IsNullOrEmpty(json)) return null;

        (JObject payload, IReadOnlyDictionary<Guid, Guid> remapped) = PortableIdRemapper.Remap(JObject.Parse(json));
        map = remapped;
        return payload.ToObject<CustomFolder>(ButtonSnapshot.Serializer);
    }
}
