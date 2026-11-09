namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// The suggested solution for a failed or restricted check. It is displayed; only a fix that
/// carries an <see cref="Action"/> can also be applied from the page, and only on a click.
/// <see cref="Command"/> and <see cref="Description"/> are already localized when the check
/// builds them.
/// </summary>
/// <param name="Kind">How the fix is applied.</param>
/// <param name="Description">Localized explanation of what the user has to do.</param>
/// <param name="Command">The exact command to run, or null when there is none.</param>
/// <param name="RequiresElevation">The command needs root rights.</param>
/// <param name="RequiresLogout">The change only takes effect after signing out and back in.</param>
/// <param name="RequiresReconnect">The device has to be unplugged and reconnected.</param>
/// <param name="Action">
/// A repair the app can carry out itself for the user, offered as a button. Never one that needs
/// root rights: those stay commands the user runs.
/// </param>
public sealed record DiagnosticFix(
    FixKind Kind,
    string Description,
    string Command = null,
    bool RequiresElevation = false,
    bool RequiresLogout = false,
    bool RequiresReconnect = false,
    DiagnosticFixAction Action = null);

/// <summary>A repair the diagnostics page runs on a click; the check is run again afterwards.</summary>
/// <param name="Label">Localized button text.</param>
/// <param name="RunAsync">The repair. A thrown exception is shown to the user.</param>
public sealed record DiagnosticFixAction(string Label, Func<CancellationToken, Task> RunAsync);
