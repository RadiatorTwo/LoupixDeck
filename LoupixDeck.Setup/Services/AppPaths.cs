namespace LoupixDeck.Setup.Services;

/// <summary>
/// Central place for the filesystem/registry locations the setup touches. The config and
/// user-plugins paths mirror the running app's resolution
/// (<c>LoupixDeck\Utils\FileDialogHelper.GetConfigDir()</c> and the dual plugin roots in
/// <c>Services\Plugins\PluginManager.cs</c>) — RELEASE semantics only (no <c>debug</c> suffix),
/// since a setup only ever deals with release builds.
/// </summary>
public static class AppPaths
{
    /// <summary>Product name used for the install folder, shortcuts and the uninstall key.</summary>
    public const string ProductName = "LoupixDeck";

    /// <summary>Publisher shown in Windows "Installed apps".</summary>
    public const string Publisher = "RadiatorTwo";

    /// <summary>Main application executable inside the install directory.</summary>
    public const string AppExeName = "LoupixDeck.exe";

    /// <summary>Dedicated uninstaller (a tiny standalone exe) written into the install dir.</summary>
    public const string UninstallerExeName = "LoupixDeck-Uninstall.exe";

    /// <summary>Small manifest written into the install dir describing what was installed.</summary>
    public const string InstallManifestName = "install-manifest.json";

    /// <summary>Folder inside a payload that holds plugin subfolders (relocated to the user dir).</summary>
    public const string PayloadPluginsFolder = "plugins";

    /// <summary>
    /// User config root: <c>(HOME ?? %USERPROFILE%)\.config\LoupixDeck</c>. Never written or
    /// removed by install/update/repair; only removed by uninstall when the user opts in.
    /// </summary>
    public static string ConfigRoot()
    {
        string home = Environment.GetEnvironmentVariable("HOME")
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", ProductName);
    }

    /// <summary>User plugins root: <c>&lt;ConfigRoot&gt;\plugins</c>. Where the setup installs plugins.</summary>
    public static string UserPluginsRoot() => Path.Combine(ConfigRoot(), "plugins");

    /// <summary>Default install directory: <c>%LOCALAPPDATA%\LoupixDeck</c> (per-user, no admin).</summary>
    public static string DefaultInstallDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    /// <summary>Per-user Start-menu Programs directory.</summary>
    public static string StartMenuProgramsDir()
        => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    /// <summary>Current user's Desktop directory.</summary>
    public static string DesktopDir()
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Start-menu / desktop shortcut file name.</summary>
    public const string ShortcutFileName = "LoupixDeck.lnk";
}
