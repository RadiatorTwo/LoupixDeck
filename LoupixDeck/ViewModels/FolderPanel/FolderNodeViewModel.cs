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

    /// <summary>Width one tree level indents a row by.</summary>
    public const double IndentStep = 14;

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

    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>True for the folder currently shown on the device.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>True for a folder the current one was opened through.</summary>
    [ObservableProperty]
    public partial bool IsInPath { get; set; }

    /// <summary>Level below the workspace root; top-level folders are 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentWidth))]
    public partial int Depth { get; set; }

    public double IndentWidth => Depth * IndentStep;

    /// <summary>Number of configured keys in the folder's layout; empty when there are none.</summary>
    [ObservableProperty]
    public partial string ActionCount { get; set; } = string.Empty;

    /// <summary>The active in-place rename, or null. A new session per edit remounts the text box.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial FolderRenameSession RenameSession { get; set; }

    public bool IsEditing => RenameSession != null;

    /// <summary>This node and every node below it, depth first.</summary>
    public IEnumerable<FolderNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (FolderNodeViewModel child in Children)
            foreach (FolderNodeViewModel nested in child.SelfAndDescendants())
                yield return nested;
    }
}

/// <summary>The edit buffer of one in-place folder rename.</summary>
public sealed partial class FolderRenameSession(FolderNodeViewModel node, string text) : ObservableObject
{
    public FolderNodeViewModel Node { get; } = node;

    [ObservableProperty]
    public partial string Text { get; set; } = text;
}
