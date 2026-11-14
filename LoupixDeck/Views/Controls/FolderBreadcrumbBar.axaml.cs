using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoupixDeck.Views.Devices;

namespace LoupixDeck.Views.Controls;

/// <summary>
/// Breadcrumbs of the open custom folders (issue #249), shown by the device layouts in place of the
/// touch pager while a folder is open. Each segment jumps straight to its level. Next to them sits
/// the pager of the open folder's own pages (issue #603).
/// </summary>
public partial class FolderBreadcrumbBar : UserControl
{
    public FolderBreadcrumbBar()
    {
        InitializeComponent();
    }

    private void OnPageNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PageNameEditing.Save(sender);
        e.Handled = true;
    }

    private void OnPageNameCommit(object sender, RoutedEventArgs e) => PageNameEditing.Save(sender);
}
