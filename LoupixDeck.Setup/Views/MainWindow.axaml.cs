using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LoupixDeck.Setup.ViewModels;

namespace LoupixDeck.Setup.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Button? browse = this.FindControl<Button>("BrowseButton");
        if (browse != null)
            browse.Click += OnBrowseClick;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Folder picker is inherently bound to the window's TopLevel, so it lives in code-behind and only
    /// writes the chosen path back to the view model — no business logic here.
    /// </summary>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WizardViewModel vm)
            return;

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select install location",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            string? path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                // Install into a LoupixDeck subfolder of the picked directory.
                vm.InstallDir = System.IO.Path.Combine(path, Services.AppPaths.ProductName);
            }
        }
    }
}
