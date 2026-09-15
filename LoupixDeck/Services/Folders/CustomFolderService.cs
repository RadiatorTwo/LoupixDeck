using LoupixDeck.Models;

namespace LoupixDeck.Services.Folders;

/// <summary>What deleting a folder removes, for the confirmation dialog.</summary>
/// <param name="Subfolders">Folders below the deleted one.</param>
/// <param name="HasContent">True when the folder or a subfolder holds configured buttons or subfolders.</param>
/// <param name="Links">Buttons and wraps elsewhere in the workspace that open one of the removed folders.</param>
public sealed record FolderDeleteImpact(int Subfolders, bool HasContent, int Links);

/// <summary>
/// Edits the custom folder tree of the active workspace (issue #249). Like
/// <see cref="IProfileEditingService"/> it only changes the model; callers save the config.
/// </summary>
public interface ICustomFolderService
{
    /// <summary>Raised after any change to the tree of the active workspace (add, rename, delete, move).</summary>
    event Action StructureChanged;

    /// <summary>Adds a folder below <paramref name="parent"/>, or at the top level when it is null.</summary>
    CustomFolder Create(CustomFolder parent, string name);

    void Rename(CustomFolder folder, string name);

    FolderDeleteImpact GetDeleteImpact(CustomFolder folder);

    /// <summary>Removes the folder and its subtree, clears every link to them and closes them if open.</summary>
    Task Delete(CustomFolder folder);

    /// <summary>True when <paramref name="folder"/> may be moved below <paramref name="newParent"/> (null = top level).</summary>
    bool CanMove(CustomFolder folder, CustomFolder newParent);

    /// <summary>Moves the folder below <paramref name="newParent"/> at <paramref name="index"/> (clamped).</summary>
    bool Move(CustomFolder folder, CustomFolder newParent, int index);

    /// <summary>
    /// True when a button of the layout currently shown may open <paramref name="folderId"/>: the
    /// folder exists and is neither the open folder nor one it was opened from.
    /// </summary>
    bool CanLink(Guid folderId);
}

public sealed class CustomFolderService(LoupedeckConfig config, IPageManager pageManager) : ICustomFolderService
{
    public event Action StructureChanged;

    public CustomFolder Create(CustomFolder parent, string name)
    {
        Workspace workspace = config.ActiveWorkspace;
        if (workspace == null) return null;

        var target = parent == null ? workspace.Folders : parent.Children;
        if (target == null) return null;

        CustomFolder folder = new() { Name = name?.Trim() ?? string.Empty };
        target.Add(folder);
        StructureChanged?.Invoke();
        return folder;
    }

    public void Rename(CustomFolder folder, string name)
    {
        if (folder == null) return;

        string oldName = folder.Name;
        string newName = name?.Trim() ?? string.Empty;
        if (oldName == newName) return;

        folder.Name = newName;

        // Folder buttons carry the name as a text layer. Follow the rename where that text still
        // is the old name; a label the user rewrote is theirs and stays.
        Workspace workspace = config.ActiveWorkspace;
        foreach (TouchButtonPage layout in workspace?.EnumerateTouchLayouts() ?? [])
        foreach (TouchButton button in layout.TouchButtons)
        foreach (ButtonState state in button?.States ?? [])
        {
            if (state == null || !FolderCommand.ReferencedFolders(state.Command).Contains(folder.Id)) continue;
            foreach (Models.Layers.TextLayer text in state.Layers?.OfType<Models.Layers.TextLayer>() ?? [])
                if (text.Text == oldName)
                    text.Text = newName;
        }

        StructureChanged?.Invoke();
    }

    public FolderDeleteImpact GetDeleteImpact(CustomFolder folder)
    {
        if (folder == null) return new FolderDeleteImpact(0, false, 0);

        List<CustomFolder> removed = [.. folder.SelfAndDescendants()];
        HashSet<Guid> ids = [.. removed.Select(static f => f.Id)];

        bool hasContent = folder.Children is { Count: > 0 } ||
                          removed.Any(f => f.Layout?.TouchButtons.Any(b => b != null && !b.IsFolderBackSlot && !ButtonSnapshot.IsEmpty(b)) == true);

        int links = 0;
        Workspace workspace = config.ActiveWorkspace;
        HashSet<TouchButtonPage> removedLayouts = [.. removed.Where(static f => f.Layout != null).Select(static f => f.Layout)];
        foreach (TouchButtonPage layout in workspace?.EnumerateTouchLayouts() ?? [])
        {
            if (removedLayouts.Contains(layout)) continue;
            links += layout.TouchButtons.Count(b => b?.States?.Any(s => FolderCommand.ReferencedFolders(s?.Command).Any(ids.Contains)) == true);
        }

        links += config.SimpleButtons?.Count(b => b?.States?.Any(s => FolderCommand.ReferencedFolders(s?.Command).Any(ids.Contains)) == true) ?? 0;

        return new FolderDeleteImpact(removed.Count - 1, hasContent, links);
    }

    public async Task Delete(CustomFolder folder)
    {
        Workspace workspace = config.ActiveWorkspace;
        var siblings = FolderTree.SiblingsOf(workspace, folder);
        if (siblings == null) return;

        HashSet<Guid> ids = [.. folder.SelfAndDescendants().Select(static f => f.Id)];

        // Leave the removed folders first, so the grid never shows a layout that is gone.
        int keep = workspace.FolderPath.TakeWhile(f => !ids.Contains(f.Id)).Count();
        if (keep < workspace.FolderPath.Count)
            await pageManager.NavigateFolderDepth(keep);

        siblings.Remove(folder);
        FolderReferenceCleaner.Clean(workspace, ids, config.SimpleButtons);
        StructureChanged?.Invoke();
    }

    public bool CanMove(CustomFolder folder, CustomFolder newParent)
    {
        Workspace workspace = config.ActiveWorkspace;
        if (folder == null || FolderTree.SiblingsOf(workspace, folder) == null) return false;
        return newParent == null || (!FolderTree.IsSelfOrDescendant(folder, newParent) &&
                                     FolderTree.SiblingsOf(workspace, newParent) != null);
    }

    public bool Move(CustomFolder folder, CustomFolder newParent, int index)
    {
        if (!CanMove(folder, newParent)) return false;

        Workspace workspace = config.ActiveWorkspace;
        var source = FolderTree.SiblingsOf(workspace, folder);
        var target = newParent == null ? workspace.Folders : newParent.Children;

        int oldIndex = source.IndexOf(folder);
        if (ReferenceEquals(source, target))
        {
            // Removing first shifts every later position down by one.
            int adjusted = Math.Clamp(index > oldIndex ? index - 1 : index, 0, target.Count - 1);
            if (adjusted == oldIndex) return false;
            target.Move(oldIndex, adjusted);
        }
        else
        {
            source.RemoveAt(oldIndex);
            target.Insert(Math.Clamp(index, 0, target.Count), folder);
        }

        // An open path stays valid: it records how the user got there, and every folder in it still exists.
        StructureChanged?.Invoke();
        return true;
    }

    public bool CanLink(Guid folderId)
    {
        Workspace workspace = config.ActiveWorkspace;
        if (FolderTree.Find(workspace, folderId) == null) return false;
        return workspace.FolderPath.All(f => f.Id != folderId);
    }
}
