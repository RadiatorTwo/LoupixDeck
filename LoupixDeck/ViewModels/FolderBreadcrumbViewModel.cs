namespace LoupixDeck.ViewModels;

/// <summary>One segment of the custom folder breadcrumb bar (issue #249).</summary>
/// <param name="Name">Workspace name for the root, folder name otherwise.</param>
/// <param name="Depth">Folder depth this segment navigates to; 0 closes every folder.</param>
/// <param name="IsCurrent">True for the last segment, the layout currently shown.</param>
public sealed record FolderBreadcrumbViewModel(string Name, int Depth, bool IsCurrent)
{
    /// <summary>Every segment but the first is preceded by a separator.</summary>
    public bool ShowSeparator => Depth > 0;
}
