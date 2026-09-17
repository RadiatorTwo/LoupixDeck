namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// The suggested solution for a failed or restricted check. Phase 1 only displays it -
/// nothing is executed. <see cref="Command"/> and <see cref="Description"/> are already
/// localized when the check builds them.
/// </summary>
/// <param name="Kind">How the fix is applied.</param>
/// <param name="Description">Localized explanation of what the user has to do.</param>
/// <param name="Command">The exact command to run, or null when there is none.</param>
/// <param name="RequiresElevation">The command needs root rights.</param>
/// <param name="RequiresLogout">The change only takes effect after signing out and back in.</param>
/// <param name="RequiresReconnect">The device has to be unplugged and reconnected.</param>
public sealed record DiagnosticFix(
    FixKind Kind,
    string Description,
    string Command = null,
    bool RequiresElevation = false,
    bool RequiresLogout = false,
    bool RequiresReconnect = false);
