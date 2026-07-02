namespace LoupixDeck.Services;

/// <summary>
/// Toggles whether the application launches automatically at user login. On Windows this maps to the
/// per-user HKCU Run entry (the same one the installer manages); on other platforms it is a no-op.
/// </summary>
public interface IAutostartService
{
    /// <summary>True when the autostart entry currently exists.</summary>
    bool IsEnabled();

    /// <summary>Enables or disables launching the app at login.</summary>
    void SetEnabled(bool enabled);
}
