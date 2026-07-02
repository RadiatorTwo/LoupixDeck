namespace LoupixDeck.Setup.Services;

/// <summary>
/// Stable identifiers for the logical steps an install/update/repair walks through. The service tags
/// each <see cref="ProgressReport"/> with one so the UI can drive a discrete step timeline, independent
/// of the free-form status detail line (which reports the current file etc.).
/// </summary>
public static class SetupSteps
{
    public const string Prepare = "prepare";
    public const string StopApp = "stop";
    public const string Backup = "backup";
    public const string Config = "config";       // delete config with backup (repair)
    public const string Files = "files";         // application/program files
    public const string Plugins = "plugins";
    public const string Uninstaller = "uninstaller";
    public const string Shortcuts = "shortcuts";
    public const string Autostart = "autostart";
    public const string Register = "register";
    public const string Cleanup = "cleanup";
    public const string Finalize = "finalize";
}
