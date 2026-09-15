using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.ActionPanel;

namespace LoupixDeck.ViewModels.FolderPanel;

/// <summary>
/// One folder in the folder panel tree (issue #249). A panel row like the apps and commands rows,
/// so the drag machine carries it onto a key the same way.
/// </summary>
public sealed partial class FolderNodeViewModel : PanelItemViewModel
{
    /// <summary>mdi-folder.</summary>
    public const string FolderGlyph = "\U000F024B";

    public FolderNodeViewModel(CustomFolder folder, FolderNodeViewModel parent)
    {
        Folder = folder;
        Parent = parent;
        Glyph = FolderGlyph;
    }

    public CustomFolder Folder { get; }

    public FolderNodeViewModel Parent { get; }

    public override string Title => Folder.Name;

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>True for the folder currently shown on the device.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>True for a folder the current one was opened through.</summary>
    [ObservableProperty]
    public partial bool IsInPath { get; set; }

    /// <summary>This node and every node below it, depth first.</summary>
    public IEnumerable<FolderNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (FolderNodeViewModel child in Children)
            foreach (FolderNodeViewModel nested in child.SelfAndDescendants())
                yield return nested;
    }
}
