using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LoupixDeck.Utils;

public abstract class FileDialogHelper
{
    public static async Task<string> OpenFileDialog()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return null;

        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Image File",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Pictures")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff"]
                },
                new("All files")
                {
                    Patterns = ["*"]
                }
            }
        });
        
        if (files.Count == 0) return string.Empty;
        
        return Uri.UnescapeDataString(files[0].Path.AbsolutePath);
    }

    public static string GetConfigPath(string fileName)
    {
        var homePath = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#if DEBUG
        var configDir = Path.Combine(homePath, ".config", "LoupixDeck", "debug");
#else
        var configDir = Path.Combine(homePath, ".config", "LoupixDeck");
#endif

        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        return Path.Combine(configDir, fileName);
    }
}