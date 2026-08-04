using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LoupixDeck.Models.Portable;

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
    /// Picks an animated source for a button (issue #121): an animated image (GIF/WebP) or a video
    /// (transcoded once on import). Returns the absolute path, an empty string if cancelled, or null
    /// when there's no window.
    /// </summary>
    public static async Task<string> OpenAnimatedImageDialog()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return null;

        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Animated Image or Video",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Animated images & videos")
                {
                    Patterns = ["*.gif", "*.webp", "*.mp4", "*.webm", "*.mov", "*.mkv", "*.m4v", "*.avi"]
                },
                new("All files")
                {
                    Patterns = ["*"]
                }
            }
        });

        if (files.Count == 0) return string.Empty;

        var file = files[0];
        var local = file.TryGetLocalPath();
        return !string.IsNullOrEmpty(local)
            ? local
            : Uri.UnescapeDataString(file.Path.AbsolutePath);
    }

    /// <summary>
    /// Picks a screensaver clip (video or animated GIF). Parented to <paramref name="owner"/>
    /// when given (the open settings dialog), falling back to the main window. Returns the
    /// absolute path, an empty string if cancelled, or null when there's no window.
    /// </summary>
    public static async Task<string> OpenVideoDialog(Window owner = null)
    {
        owner ??= WindowHelper.GetMainWindow();
        if (owner == null) return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Screensaver Video",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Videos")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mov", "*.mkv", "*.m4v", "*.avi", "*.gif"]
                },
                new("All files")
                {
                    Patterns = ["*"]
                }
            }
        });

        if (files.Count == 0) return string.Empty;

        // Prefer the real OS path (TryGetLocalPath) over the URI's AbsolutePath, which on
        // Windows yields "/C:/Users/…" — that can fail File.Exists/File.Copy and make the
        // selection silently do nothing.
        var file = files[0];
        var local = file.TryGetLocalPath();
        return !string.IsNullOrEmpty(local)
            ? local
            : Uri.UnescapeDataString(file.Path.AbsolutePath);
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

    /// <summary>
    /// Picks a <c>.loupixprofile</c> package to import. Parented to <paramref name="owner"/> when
    /// given (the open settings dialog), falling back to the main window. Returns the absolute
    /// path, an empty string if cancelled, or null when there's no window.
    /// </summary>
    public static async Task<string> OpenProfilePackageDialog(Window owner = null)
    {
        owner ??= WindowHelper.GetMainWindow();
        if (owner == null) return null;

        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Profile Package",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("LoupixDeck profile package")
                {
                    Patterns = [$"*.{ProfilePackageFiles.Extension}"]
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*"]
                }
            ]
        });

        return files.Count == 0 ? string.Empty : ResolveLocalPath(files[0]);
    }

    /// <summary>
    /// Asks where to write a <c>.loupixprofile</c> package. Returns the absolute path, an empty
    /// string if cancelled, or null when there's no window. The picker itself confirms an
    /// overwrite, so the caller may delete an existing file without asking again.
    /// </summary>
    public static async Task<string> SaveProfilePackageDialog(Window owner, string suggestedFileName)
    {
        owner ??= WindowHelper.GetMainWindow();
        if (owner == null) return null;

        IStorageFile file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Profile Package",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = ProfilePackageFiles.Extension,
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("LoupixDeck profile package")
                {
                    Patterns = [$"*.{ProfilePackageFiles.Extension}"]
                }
            ]
        });

        return file == null ? string.Empty : ResolveLocalPath(file);
    }

    /// <summary>
    /// Builds a file name for a package from an item name. Characters a file name cannot hold are
    /// replaced, so a profile called "OBS / Stream" does not produce an invalid path.
    /// </summary>
    public static string SuggestPackageFileName(string itemName)
    {
        string cleaned = string.IsNullOrWhiteSpace(itemName)
            ? "profile"
            : new string(itemName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();

        if (cleaned.Length == 0)
            cleaned = "profile";

        return $"{cleaned}.{ProfilePackageFiles.Extension}";
    }

    /// <summary>
    /// Prefer the real OS path over the URI's AbsolutePath, which on Windows yields "/C:/Users/…"
    /// and can silently break File.Exists / File.Copy.
    /// </summary>
    private static string ResolveLocalPath(IStorageFile file)
    {
        string local = file.TryGetLocalPath();
        return !string.IsNullOrEmpty(local) ? local : Uri.UnescapeDataString(file.Path.AbsolutePath);
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

    /// <summary>
    /// Path to the per-instance config file, scoped by device type AND serial
    /// (e.g. config_loupedeck-live-s_rz2004.json). Falls back to the slug-only path
    /// when the device has no usable serial, so a device without a real iSerial
    /// behaves exactly as before. Use this for everything except first-launch
    /// detection / legacy migration.
    /// </summary>
    public static string GetConfigPath(LoupixDeck.Registry.DeviceRegistry.DeviceInfo deviceInfo, string serial)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        var safe = SerialNormalizer.ForFilename(serial);
        return string.IsNullOrEmpty(safe)
            ? GetConfigPath(deviceInfo)
            : Path.Combine(GetConfigDir(), $"config_{deviceInfo.Slug}_{safe}.json");
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