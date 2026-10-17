using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LoupixDeck.ViewModels.FolderPanel;

namespace LoupixDeck.Views;

/// <summary>
/// The content of the custom folder panel (issue #249): the folder tree with its header, search
/// and footer. Presses and drags on the rows are handled by the panel's host.
/// </summary>
public partial class FolderPanelView : UserControl
{
    public FolderPanelView()
    {
        InitializeComponent();
    }

    private FolderPanelViewModel ViewModel => DataContext as FolderPanelViewModel;

    private void OnRootTapped(object sender, TappedEventArgs e)
        => _ = ViewModel?.CloseFoldersCommand.ExecuteAsync(null);

    // ── In-place rename ────────────────────────────────────────────────────

    private void OnRenameBoxAttached(object sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box) return;

        // The box is created per rename session, so focusing it on attach starts each edit fresh.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FolderRenameSession session } box) return;

        switch (e.Key)
        {
            case Key.Enter:
                ViewModel?.CommitRename(session, box.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel?.CancelRename(session);
                e.Handled = true;
                break;
        }
    }

    private void OnRenameBoxLostFocus(object sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: FolderRenameSession session } box)
            ViewModel?.CommitRename(session, box.Text);
    }
}
