using Tmds.DBus;

namespace LoupixDeck.Services.AppActivation;

/// <summary>GNOME Shell's extension manager, as the Extensions app and the browser connector use it.</summary>
[DBusInterface("org.gnome.Shell.Extensions")]
public interface IGnomeShellExtensions : IDBusObject
{
    Task<IDictionary<string, object>> GetExtensionInfoAsync(string uuid);

    Task<string> InstallRemoteExtensionAsync(string uuid);

    Task<bool> EnableExtensionAsync(string uuid);

    Task<T> GetAsync<T>(string prop);

    Task SetAsync(string prop, object val);
}

/// <summary>
/// The D-Bus interface of the "Activate Window By Title" extension. The member names are the
/// extension's own lower-camel-case names: Tmds.DBus derives the D-Bus member from the C# name
/// minus "Async", so they cannot follow the C# casing convention.
/// </summary>
[DBusInterface("de.lucaswerkmeister.ActivateWindowByTitle")]
public interface IActivateWindowByTitle : IDBusObject
{
#pragma warning disable IDE1006 // D-Bus member names, see the interface summary.
    Task<bool> activateByWmClassAsync(string name);

    Task<bool> activateByWmClassInstanceAsync(string instance);

    Task<string> setSortOrderAsync(string newSortOrder);
#pragma warning restore IDE1006
}

/// <summary>Where the extension stands in this session, as far as LoupixDeck is concerned.</summary>
public enum GnomeExtensionState
{
    /// <summary>The session bus or GNOME Shell could not be reached.</summary>
    ShellUnavailable,

    /// <summary>The extension is not installed.</summary>
    NotInstalled,

    /// <summary>All user extensions are switched off in the Extensions app.</summary>
    UserExtensionsDisabled,

    /// <summary>Installed but not enabled.</summary>
    Disabled,

    /// <summary>Installed, but GNOME Shell failed to load it or does not consider it compatible.</summary>
    Broken,

    /// <summary>Enabled and running.</summary>
    Active
}

/// <summary>
/// GNOME on Wayland gives no application a way to focus another application's window: the
/// Shell's own introspection interface is reserved for the desktop portal, and nothing in it
/// activates a window. The "Activate Window By Title" extension runs inside the Shell and
/// exports exactly that over D-Bus. This class finds out whether it is usable and installs or
/// enables it through GNOME Shell itself, which shows its own confirmation dialog.
/// </summary>
public static class GnomeActivationExtension
{
    public const string Uuid = "activate-window-by-title@lucaswerkmeister.de";

    public const string PageUrl = "https://extensions.gnome.org/extension/5021/activate-window-by-title/";

    public const string ShellService = "org.gnome.Shell";

    private const string ShellPath = "/org/gnome/Shell";

    public const string ExtensionPath = "/de/lucaswerkmeister/ActivateWindowByTitle";

    // GNOME Shell's ExtensionState values (js/misc/extensionUtils.js).
    private const int StateActive = 1;
    private const int StateInactive = 2;
    private const int StateError = 3;
    private const int StateOutOfDate = 4;
    private const int StateInitialized = 6;
    private const int StateUninstalled = 99;

    /// <summary>The command a user can run to install it by hand; opens the same Shell dialog.</summary>
    public static string InstallCommand =>
        $"gdbus call --session --dest {ShellService} --object-path {ShellPath} " +
        $"--method org.gnome.Shell.Extensions.InstallRemoteExtension {Uuid}";

    public static string EnableCommand => $"gnome-extensions enable {Uuid}";

    public const string EnableUserExtensionsCommand = "gsettings set org.gnome.shell disable-user-extensions false";

    /// <summary>True on a GNOME session (the desktop, not its session type).</summary>
    public static bool IsGnome()
        => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
            .Split(':').Any(part => part.Equals("GNOME", StringComparison.OrdinalIgnoreCase));

    /// <summary>The extension's state, plus GNOME Shell's raw state number for a report.</summary>
    public static async Task<(GnomeExtensionState State, string Raw)> QueryAsync(CancellationToken cancellationToken)
    {
        using Connection connection = await ConnectAsync(cancellationToken);
        if (connection == null)
            return (GnomeExtensionState.ShellUnavailable, null);

        IGnomeShellExtensions shell = Extensions(connection);

        if (!await shell.GetAsync<bool>("UserExtensionsEnabled"))
            return (GnomeExtensionState.UserExtensionsDisabled, null);

        IDictionary<string, object> info = await shell.GetExtensionInfoAsync(Uuid);
        if (info == null || !info.TryGetValue("state", out object raw))
            return (GnomeExtensionState.NotInstalled, null);

        int state = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
        string text = state.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return state switch
        {
            StateActive => (GnomeExtensionState.Active, text),
            StateInactive or StateInitialized => (GnomeExtensionState.Disabled, text),
            StateError or StateOutOfDate => (GnomeExtensionState.Broken, text),
            StateUninstalled => (GnomeExtensionState.NotInstalled, text),
            _ => (GnomeExtensionState.Broken, text)
        };
    }

    /// <summary>
    /// Asks GNOME Shell to download and install the extension. The Shell shows its own dialog and
    /// enables the extension once the user confirms; the reply says whether they did.
    /// </summary>
    public static async Task<string> InstallAsync(CancellationToken cancellationToken)
    {
        using Connection connection = await RequireConnectionAsync(cancellationToken);
        return await Extensions(connection).InstallRemoteExtensionAsync(Uuid);
    }

    public static async Task EnableAsync(CancellationToken cancellationToken)
    {
        using Connection connection = await RequireConnectionAsync(cancellationToken);
        if (!await Extensions(connection).EnableExtensionAsync(Uuid))
            throw new InvalidOperationException("GNOME Shell refused to enable the extension.");
    }

    public static async Task EnableUserExtensionsAsync(CancellationToken cancellationToken)
    {
        using Connection connection = await RequireConnectionAsync(cancellationToken);
        await Extensions(connection).SetAsync("UserExtensionsEnabled", true);
    }

    /// <summary>A connected session bus with GNOME Shell on it, or null when there is none.</summary>
    internal static async Task<Connection> ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return null;

        Connection connection = new(Address.Session);
        try
        {
            await connection.ConnectAsync().WaitAsync(cancellationToken);
            if (await connection.IsServiceActiveAsync(ShellService).WaitAsync(cancellationToken))
                return connection;
        }
        catch (Exception ex) when (ex is DBusException or ConnectException or InvalidOperationException)
        {
            Console.WriteLine($"[GnomeActivation] Session bus not usable: {ex.Message}");
        }

        connection.Dispose();
        return null;
    }

    private static async Task<Connection> RequireConnectionAsync(CancellationToken cancellationToken)
        => await ConnectAsync(cancellationToken)
           ?? throw new InvalidOperationException("GNOME Shell is not reachable on the session bus.");

    private static IGnomeShellExtensions Extensions(Connection connection)
        => connection.CreateProxy<IGnomeShellExtensions>(ShellService, ShellPath);
}
