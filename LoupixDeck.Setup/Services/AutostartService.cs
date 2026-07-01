using Microsoft.Win32;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Manages the per-user "run at Windows startup" entry under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Registry APIs are NativeAOT-safe, so no
/// extra trimming configuration is needed. Never requires administrator rights (HKCU only).
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppPaths.ProductName;

    /// <summary>Writes the autostart entry pointing at the installed application executable.</summary>
    public static void Set(string appExePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{appExePath}\"");
    }

    /// <summary>Removes the autostart entry if present (no error when it is absent).</summary>
    public static void Remove()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>True when an autostart entry for LoupixDeck currently exists.</summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    /// <summary>Enables or disables the autostart entry in a single call.</summary>
    public static void Apply(string appExePath, bool enabled)
    {
        if (enabled)
            Set(appExePath);
        else
            Remove();
    }
}
