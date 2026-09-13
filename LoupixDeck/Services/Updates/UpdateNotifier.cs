namespace LoupixDeck.Services.Updates;

/// <summary>Shows an operating-system notification, used while the main window sits in the tray.</summary>
public interface IUpdateNotifier
{
    /// <param name="windowHandle">Native handle of the (hidden) main window; Windows only.</param>
    void Show(string title, string body, IntPtr windowHandle);
}

/// <summary>Linux: a desktop notification over DBus (org.freedesktop.Notifications).</summary>
public sealed class DBusUpdateNotifier(IDBusController dbus) : IUpdateNotifier
{
    public void Show(string title, string body, IntPtr windowHandle)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await dbus.SendNotificationAsync(title, body, 10000);
            }
            catch (Exception ex)
            {
                // No notification daemon or session bus: the main-window hint is still there.
                Console.WriteLine($"[Update] Desktop notification failed: {ex.Message}");
            }
        });
    }
}
