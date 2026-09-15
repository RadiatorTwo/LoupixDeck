using System.Collections.ObjectModel;
using LoupixDeck.Models;

namespace LoupixDeck.Services.Folders;

/// <summary>Lookups over a workspace's custom folder tree (issue #249).</summary>
public static class FolderTree
{
    /// <summary>The folder with <paramref name="folderId"/>, or null.</summary>
    public static CustomFolder Find(Workspace workspace, Guid folderId)
        => workspace?.EnumerateFolders().FirstOrDefault(f => f.Id == folderId);

    /// <summary>
    /// The folders from the top level down to <paramref name="folderId"/>, both included, or null
    /// when the folder is not part of the workspace.
    /// </summary>
    public static List<CustomFolder> PathTo(Workspace workspace, Guid folderId)
    {
        if (workspace?.Folders == null) return null;
        List<CustomFolder> path = [];
        return Search(workspace.Folders, folderId, path) ? path : null;
    }

    /// <summary>The collection that holds <paramref name="folder"/>: its parent's children or the workspace's top level.</summary>
    public static ObservableCollection<CustomFolder> SiblingsOf(Workspace workspace, CustomFolder folder)
    {
        if (workspace?.Folders == null || folder == null) return null;
        if (workspace.Folders.Contains(folder)) return workspace.Folders;
        return workspace.EnumerateFolders().FirstOrDefault(f => f.Children?.Contains(folder) == true)?.Children;
    }

    /// <summary>The parent folder of <paramref name="folder"/>, or null for a top-level folder.</summary>
    public static CustomFolder ParentOf(Workspace workspace, CustomFolder folder)
        => workspace?.EnumerateFolders().FirstOrDefault(f => f.Children?.Contains(folder) == true);

    /// <summary>True when <paramref name="candidate"/> is <paramref name="folder"/> or lies below it.</summary>
    public static bool IsSelfOrDescendant(CustomFolder folder, CustomFolder candidate)
        => folder != null && candidate != null && folder.SelfAndDescendants().Contains(candidate);

    private static bool Search(IEnumerable<CustomFolder> level, Guid folderId, List<CustomFolder> path)
    {
        foreach (CustomFolder folder in level ?? [])
        {
            if (folder == null) continue;
            path.Add(folder);
            if (folder.Id == folderId || Search(folder.Children, folderId, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }
}
