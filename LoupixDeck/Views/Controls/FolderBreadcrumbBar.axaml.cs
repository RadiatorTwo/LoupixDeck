using Avalonia.Controls;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Breadcrumbs of the open custom folders (issue #249), shown by the device layouts in place of the
/// touch pager while a folder is open. Each segment jumps straight to its level.
/// </summary>
public partial class FolderBreadcrumbBar : UserControl
{
    public FolderBreadcrumbBar()
    {
        InitializeComponent();
    }
}
