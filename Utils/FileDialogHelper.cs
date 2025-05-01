using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LoupixDeck.Utils;

public abstract class FileDialogHelper
{
    public static async Task<IStorageFile> OpenFileDialog()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return null;
        
        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Datei auswählen",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Bilder")
                {
                    Patterns = ["*.png","*.jpg","*.jpeg","*.bmp","*.tif","*.tiff"]
                },
                new("Alle Dateien")
                {
                    Patterns = ["*"]
                }
            }
        });

        return files.Count > 0 ? files[0] : null;
    }
    
    public static string GetConfigPath(string fileName)
    {
        var homePath = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var configDir = Path.Combine(homePath, ".config", "LoupixDeck");

        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        return Path.Combine(configDir, fileName);
    }
}