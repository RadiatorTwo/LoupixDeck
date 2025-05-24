using Tmds.DBus;

namespace LoupixDeck.Services;

[DBusInterface("org.freedesktop.Notifications")]
public interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon,
        string summary, string body, string[] actions,
        IDictionary<string, object> hints, int expireTimeout);
}

public interface IDBusController
{
    Task SendNotificationAsync(string title, 
        string body,
        int expireTimeout = 5000);
}

public class DBusController : IDBusController
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