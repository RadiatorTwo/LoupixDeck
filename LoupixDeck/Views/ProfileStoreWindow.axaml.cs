using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels.ProfileStore;

namespace LoupixDeck.Views;

/// <summary>The community profile store (issue #587).</summary>
public partial class ProfileStoreWindow : Window
{
    public ProfileStoreWindow() : this(null) { }

    // Preferred ctor (see DialogService): DataContext is set before the XAML pass so the
    // $parent[ItemsControl].DataContext bindings in the tile template have a target.
    public ProfileStoreWindow(ProfileStoreViewModel viewModel)
    {
        if (viewModel != null)
        {
            DataContext = viewModel;
            viewModel.CloseWindow += Close;
        }

        InitializeComponent();

        Closed += (_, _) =>
        {
            if (DataContext is not ProfileStoreViewModel vm)
                return;

            vm.OnClosed();
            vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
