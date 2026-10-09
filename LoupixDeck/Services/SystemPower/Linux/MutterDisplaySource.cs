using Tmds.DBus;

namespace LoupixDeck.Services.SystemPower.Linux;

[DBusInterface("org.gnome.Mutter.DisplayConfig")]
public interface IMutterDisplayConfig : IDBusObject
{
    Task<T> GetAsync<T>(string prop);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

/// <summary>
/// GNOME: Mutter's <c>PowerSaveMode</c> property (0 = on, 1 standby, 2 suspend, 3 off, -1 unknown),
/// announced through <c>PropertiesChanged</c>. It is what gnome-settings-daemon itself switches,
/// so it is the actual monitor power state on X11 and Wayland alike.
/// </summary>
public sealed class MutterDisplaySource : ILinuxDisplaySource
{
    private const string Service = "org.gnome.Mutter.DisplayConfig";
    private const string PowerSaveMode = "PowerSaveMode";

    private Connection _connection;
    private IDisposable _watch;

    public string Name => "Mutter PowerSaveMode";

    public async Task<bool> TryStartAsync(Action<bool> report)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return false;

        _connection = new Connection(Address.Session);
        await _connection.ConnectAsync();
        if (!await _connection.IsServiceActiveAsync(Service)) return false;

        IMutterDisplayConfig config = _connection.CreateProxy<IMutterDisplayConfig>(
            Service, "/org/gnome/Mutter/DisplayConfig");

        // Older Mutter versions lack the property; the read throws and the next source is tried.
        int mode = await config.GetAsync<int>(PowerSaveMode);

        _watch = await config.WatchPropertiesAsync(changes =>
        {
            foreach (KeyValuePair<string, object> change in changes.Changed)
            {
                if (change.Key == PowerSaveMode && change.Value is int changed)
                    report(IsOn(changed));
            }
        });

        report(IsOn(mode));
        return true;
    }

    // -1 means Mutter does not know; treat it as on so the device is never darkened by a guess.
    private static bool IsOn(int mode) => mode <= 0;

    public void Dispose()
    {
        _watch?.Dispose();
        _watch = null;
        _connection?.Dispose();
        _connection = null;
    }
}
