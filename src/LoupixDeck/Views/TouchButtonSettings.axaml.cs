using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class TouchButtonSettings : Window
{
    public TouchButtonSettings()
    {
        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
            {
                vm.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.Source is TextBlock textBlock && textBlock.DataContext is MenuEntry menuEntry &&
            menuEntry.Command != null && !string.IsNullOrWhiteSpace(menuEntry.Command))
        {
            if (e.ClickCount == 2)
            {
                ((TouchButtonSettingsViewModel)DataContext)?.InsertCommand(menuEntry);
            }
        }
        else
        {
            var source = e.Source as Control;
            var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();

            if (treeViewItem == null || !e.GetCurrentPoint(treeViewItem).Properties.IsLeftButtonPressed) return;
            var menuEntryP = (MenuEntry)treeViewItem.DataContext;

            if (menuEntryP == null)
            {
                e.Handled = true;
                return;
            }

            if (menuEntryP.Command == null || !string.IsNullOrWhiteSpace(menuEntryP.Command)) return;

            treeViewItem.IsExpanded = !treeViewItem.IsExpanded;

            e.Handled = true;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var listBox = this.Find<ListBox>("SvgListBox");

        if (listBox != null)
            listBox.GetObservable(BoundsProperty).Subscribe(bounds =>
            {
                if (listBox.Presenter?.Panel is WrapPanel wrapPanel)
                {
                    wrapPanel.Height = bounds.Height;
                }
            });
    }
}