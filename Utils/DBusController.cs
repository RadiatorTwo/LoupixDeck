using Tmds.DBus;

namespace LoupixDeck.Utils;

[DBusInterface("org.freedesktop.Notifications")]
public interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon,
        string summary, string body, string[] actions,
        IDictionary<string, object> hints, int expireTimeout);
}

public class DBusController
{
    public async Task SendNotificationAsync(string title, 
                                            string body,
                                            int expireTimeout = 5000)
    {
        var connection = new Connection(Address.Session);
        await connection.ConnectAsync();

        var notifications = connection.CreateProxy<INotifications>(
            "org.freedesktop.Notifications", "/org/freedesktop/Notifications");

        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LoupixDeck.ico");
        
        await notifications.NotifyAsync("LoupixDeck", 0, iconPath,
            title, body,
            [], new Dictionary<string, object>(), expireTimeout);
    }
}