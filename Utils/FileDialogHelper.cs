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

    /// <summary>
    /// Picks a <c>.zip</c> plugin package. Parented to <paramref name="owner"/> when
    /// given (the open settings dialog), falling back to the main window. Returns the
    /// absolute path, an empty string if cancelled, or null when there's no window.
    /// </summary>
    public static async Task<string> OpenZipDialog(Window owner = null)
    {
        owner ??= WindowHelper.GetMainWindow();
        if (owner == null) return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Plugin Package",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Plugin package")
                {
                    Patterns = ["*.zip"]
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
        return Path.Combine(GetConfigDir(), fileName);
    }

    /// <summary>
    /// Path to the per-device config file (e.g. config_loupedeck-live-s.json).
    /// Use this for everything except first-launch detection / legacy migration.
    /// </summary>
    public static string GetConfigPath(LoupixDeck.Registry.DeviceRegistry.DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return Path.Combine(GetConfigDir(), $"config_{deviceInfo.Slug}.json");
    }

    public static string GetConfigDir()
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

        return configDir;
    }
}