using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LoupixDeck.Utils;

public abstract class FileDialogHelper
{
    public static async Task<IStorageFile> OpenFileDialog(Window parent)
    {
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
}