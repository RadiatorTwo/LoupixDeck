namespace LoupixDeck.Models;

/// <summary>
/// What the main window does when the user clicks the close (X) button.
/// On desktops without a working system tray (elementaryOS/Pantheon, GNOME
/// without AppIndicator), MinimizeToTray would make the app unreachable —
/// users on those DEs should pick Quit.
/// </summary>
public enum CloseButtonBehavior
{
    MinimizeToTray,
    Quit
}
