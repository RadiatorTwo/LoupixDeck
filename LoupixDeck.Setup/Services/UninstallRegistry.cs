using Microsoft.Win32;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Registers/unregisters LoupixDeck in the per-user Windows uninstall list
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\LoupixDeck</c>) so it shows up in
/// "Installed apps" / "Programs and Features" without requiring admin rights.
/// </summary>
public static class UninstallRegistry
{
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppPaths.ProductName;

    /// <summary>Reads the install location of a previously registered install, if any.</summary>
    public static string? GetInstalledLocation()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    /// <summary>Reads the registered version of a previous install, if any.</summary>
    public static string? GetInstalledVersion()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
        return key?.GetValue("DisplayVersion") as string;
    }

    public static void Register(string installDir, string version, long estimatedSizeBytes)
    {
        string uninstallerExe = Path.Combine(installDir, AppPaths.UninstallerExeName);
        string appExe = Path.Combine(installDir, AppPaths.AppExeName);

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppPaths.ProductName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", AppPaths.Publisher);
        key.SetValue("DisplayIcon", appExe);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", $"\"{uninstallerExe}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstallerExe}\" --silent");
        // Modify/repair is not offered from Programs & Features (repair needs the payload, which lives
        // in the downloaded setup, not the install dir).
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Max(1, estimatedSizeBytes / 1024), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    /// <summary>Updates just the version after an update/repair.</summary>
    public static void UpdateVersion(string version)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: true);
        key?.SetValue("DisplayVersion", version);
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // best effort
        }
    }
}
