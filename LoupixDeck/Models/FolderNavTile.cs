namespace LoupixDeck.Models;

/// <summary>The automatic navigation tiles the host places on an open custom folder's layout.</summary>
public enum FolderNavTile
{
    /// <summary>A normal key.</summary>
    None,

    /// <summary>Bottom-left: shows the previous page, or leaves the folder on its first page.</summary>
    Back,

    /// <summary>Bottom-right, on every page of a folder but its last: shows the next page (issue #603).</summary>
    Next
}
