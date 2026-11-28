using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Registry;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Portable;

namespace LoupixDeck.Services.Folders;

/// <summary>
/// Turns a workspace into a custom folder and a custom folder into a workspace (issue #606). Both
/// directions build a deep copy with fresh ids, so the source can be kept next to the result; links
/// inside the copied tree (a page key opening one of the copied folders, a state switch) follow to
/// the copy. Only the model is built here; callers add the result, remove the source when asked and
/// save the config.
/// </summary>
public static class WorkspaceFolderConverter
{
    /// <summary>
    /// A folder holding the touch pages and folders of <paramref name="workspace"/>. Dial pages have
    /// no place in a folder and are not carried over. Keys a navigation tile would cover (Back on
    /// every page, Next on every page but the last) move to a free key, on a later page when their
    /// own page is full. A folder has no background of its own, so the pages' wallpapers are
    /// dropped (issue #706).
    /// </summary>
    /// <param name="movedKeys">Number of keys that had to move for a navigation tile.</param>
    public static CustomFolder ToFolder(Workspace workspace, FolderGrid grid, int keyCount, DeviceGeometry geometry,
        out int movedKeys)
    {
        movedKeys = 0;
        if (workspace == null) return null;

        // References only: the snapshot below makes the copy, the workspace is left untouched.
        CustomFolder template = new() { Name = workspace.Name };
        List<TouchButtonPage> pages = [.. (workspace.TouchButtonPages ?? []).Where(static p => p != null)];
        if (pages.Count > 0)
        {
            template.Layout = pages[0];
            template.ExtraPages = pages.GetRange(1, pages.Count - 1);
        }

        foreach (CustomFolder child in workspace.Folders ?? [])
            if (child != null)
                template.Children.Add(child);

        CustomFolder folder = FolderSnapshot.Instantiate(FolderSnapshot.Capture(template), out _);
        if (folder == null) return null;

        Fit(folder, keyCount, geometry);
        ClearWallpapers(folder);
        movedKeys = MakeRoomForNavigationTiles(folder, grid, keyCount, geometry);
        return folder;
    }

    /// <summary>
    /// A workspace whose pages are the pages of <paramref name="folder"/> and whose folders are its
    /// sub-folders. Links to folders outside the folder's subtree are removed, since the new
    /// workspace does not have them.
    /// </summary>
    public static Workspace ToWorkspace(CustomFolder folder, int keyCount, DeviceGeometry geometry)
    {
        if (folder == null) return null;

        CustomFolder copy = FolderSnapshot.Instantiate(FolderSnapshot.Capture(folder), out _);
        if (copy == null) return null;

        Fit(copy, keyCount, geometry);

        Workspace workspace = new() { Name = copy.Name };
        int number = 1;
        foreach (TouchButtonPage page in copy.Pages())
        {
            page.Page = number++;
            workspace.TouchButtonPages.Add(page);
        }

        foreach (CustomFolder child in copy.Children ?? [])
            if (child != null)
                workspace.Folders.Add(child);

        FolderReferenceCleaner.CleanDangling(workspace);
        return workspace;
    }

    /// <summary>Pads every layout of the tree to this device's key count and assigns its geometry.</summary>
    private static void Fit(CustomFolder root, int keyCount, DeviceGeometry geometry)
    {
        foreach (CustomFolder folder in root.SelfAndDescendants())
        foreach (TouchButtonPage layout in folder.Pages())
        {
            PortablePayloadNormalizer.Normalize(layout, keyCount);
            layout.Geometry = geometry;
        }
    }

    /// <summary>Removes every wallpaper from the pages of the tree.</summary>
    private static void ClearWallpapers(CustomFolder root)
    {
        foreach (CustomFolder folder in root.SelfAndDescendants())
        foreach (TouchButtonPage layout in folder.Pages())
        {
            layout.MainWallpaper.Clear();
            layout.LeftWallpaper.Clear();
            layout.RightWallpaper.Clear();
        }
    }

    /// <summary>
    /// Empties the keys the folder's navigation tiles will cover. Their content goes to the first
    /// free key of the same page, else of a later page; a page is added at the end when no page has
    /// room left. Returns the number of keys that moved.
    /// </summary>
    private static int MakeRoomForNavigationTiles(CustomFolder folder, FolderGrid grid, int keyCount, DeviceGeometry geometry)
    {
        List<TouchButtonPage> pages = [.. folder.Pages()];
        if (pages.Count == 0) return 0;

        int backIndex = grid.BackSlotIndex;
        int nextIndex = grid.NextSlotIndex;
        int moved = 0;
        List<TouchButton> pending = [];

        for (int i = 0; i < pages.Count; i++)
        {
            TouchButtonPage page = pages[i];

            Take(page, backIndex, pending, ref moved);
            if (i < pages.Count - 1)
                Take(page, nextIndex, pending, ref moved);

            // The Next key is never filled, so adding a page later does not cover a moved key.
            Place(page, pending, grid, backIndex, nextIndex);

            if (pending.Count > 0 && i == pages.Count - 1)
            {
                TouchButtonPage added = new(keyCount) { Geometry = geometry };
                for (int k = 0; k < keyCount; k++)
                    added.TouchButtons[k] = new TouchButton(k);
                pages.Add(added);

                // This page is no longer the last one, so it gets a Next tile as well.
                Take(page, nextIndex, pending, ref moved);
            }
        }

        for (int i = 0; i < pages.Count; i++)
            pages[i].Page = i + 1;
        folder.Layout = pages[0];
        folder.ExtraPages = pages.GetRange(1, pages.Count - 1);
        return moved;
    }

    /// <summary>Moves the content of key <paramref name="slot"/> to <paramref name="pending"/>, leaving an empty key.</summary>
    private static void Take(TouchButtonPage page, int slot, List<TouchButton> pending, ref int moved)
    {
        TouchButton covered = page.TouchButtons.FindByIndex(slot);
        if (covered == null || ButtonSnapshot.IsEmpty(covered)) return;

        int position = page.TouchButtons.IndexOf(covered);

        page.TouchButtons[position] = new TouchButton(slot);
        pending.Add(covered);
        moved++;
    }

    /// <summary>Puts as many <paramref name="pending"/> keys as fit on the free keys of <paramref name="page"/>.</summary>
    private static void Place(TouchButtonPage page, List<TouchButton> pending, FolderGrid grid, int backIndex, int nextIndex)
    {
        for (int position = 0; position < page.TouchButtons.Count && pending.Count > 0; position++)
        {
            TouchButton free = page.TouchButtons[position];
            if (free == null || free.Index == backIndex || free.Index == nextIndex || !grid.IsGridSlot(free.Index) ||
                !ButtonSnapshot.IsEmpty(free))
                continue;

            TouchButton button = pending[0];
            pending.RemoveAt(0);
            button.Index = free.Index;
            page.TouchButtons[position] = button;
        }
    }
}
