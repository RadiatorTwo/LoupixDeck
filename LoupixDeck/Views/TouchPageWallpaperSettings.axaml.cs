using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class TouchPageWallpaperSettings : Window
{
    public TouchPageWallpaperSettings()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is TouchPageWallpaperSettingsViewModel vm)
                vm.CloseRequested += Close;
        };

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }
}
