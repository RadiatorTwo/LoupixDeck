namespace LoupixDeck.Services;

/// <summary>
/// Manages the per-user "run at Windows startup" entry under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. The value name and quoting mirror the
/// installer's own autostart handling (LoupixDeck.Setup) so the app and the installer manage the same
/// Run entry. HKCU only, so it never requires administrator rights. On non-Windows platforms every
/// call is a no-op.
/// </summary>
public sealed class AutostartService : IAutostartService
{
#if WINDOWS
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Kept identical to the installer's AppPaths.ProductName so both manage the same value.
    private const string ValueName = "LoupixDeck";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool IsEnabled()
    {
        using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
#else
    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled)
    {
        // Autostart via the Windows Run key is not applicable on this platform.
    }
#endif
}
