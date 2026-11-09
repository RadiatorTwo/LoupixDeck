using System.Runtime.Versioning;
using Tmds.DBus;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Focuses a window on GNOME (Wayland) through the "Activate Window By Title" extension (see
/// <see cref="GnomeActivationExtension"/>). Mutter knows every window, native Wayland and
/// XWayland alike, so this reaches what the X11 fallback cannot.
/// </summary>
/// <remarks>
/// The extension matches a window class exactly and reports only whether something matched, not
/// which process owns it or whether it already had focus. The candidates are therefore tried one
/// after the other — the process name, then what the launch target says the window class is —
/// against both halves of WM_CLASS; for a native Wayland window both are its app_id.
/// <para>
/// By default the extension takes the first match in stacking order, which is the bottom-most
/// window: with two windows of an application, a press would raise the one in the back. The sort
/// order is therefore switched to the most recently used window for the duration of the call and
/// restored afterwards, as it is shared with every other user of the extension. That also covers
/// an application that already has focus: its focused window is the most recently used one, and
/// activating it again changes nothing.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public static class GnomeWindowActivator
{
    /// <summary>What a call to the extension came to.</summary>
    public enum Result
    {
        /// <summary>The extension is not running in this session; another backend has to try.</summary>
        Unavailable,

        /// <summary>No window has any of the candidate classes.</summary>
        NotFound,

        Activated
    }

    private const string MostRecentFirst = "highest_user_time";

    public static async Task<Result> ActivateAsync(IReadOnlyCollection<string> windowClasses,
        CancellationToken cancellationToken)
    {
        using Connection connection = await GnomeActivationExtension.ConnectAsync(cancellationToken);
        if (connection == null)
            return Result.Unavailable;

        IActivateWindowByTitle extension = connection.CreateProxy<IActivateWindowByTitle>(
            GnomeActivationExtension.ShellService, GnomeActivationExtension.ExtensionPath);

        string previousOrder = null;
        try
        {
            previousOrder = await SetSortOrderAsync(extension, MostRecentFirst, cancellationToken);

            foreach (string windowClass in windowClasses)
            {
                if (await extension.activateByWmClassAsync(windowClass).WaitAsync(cancellationToken)
                    || await extension.activateByWmClassInstanceAsync(windowClass).WaitAsync(cancellationToken))
                {
                    return Result.Activated;
                }
            }
        }
        catch (DBusException ex) when (IsMissingInterface(ex))
        {
            return Result.Unavailable;
        }
        finally
        {
            if (previousOrder != null && previousOrder != MostRecentFirst)
                await SetSortOrderAsync(extension, previousOrder, CancellationToken.None);
        }

        return Result.NotFound;
    }

    /// <summary>
    /// Sets the extension's sort order and returns the previous one, or null when it could not be
    /// set (a release without the method): then the default order applies.
    /// </summary>
    private static async Task<string> SetSortOrderAsync(IActivateWindowByTitle extension, string order,
        CancellationToken cancellationToken)
    {
        try
        {
            return await extension.setSortOrderAsync(order).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (Exception ex) when (ex is DBusException or TimeoutException)
        {
            Console.WriteLine($"[GnomeActivation] Sort order '{order}' not set: {ex.Message}");
            return null;
        }
    }

    /// <summary>The object or method is not exported: the extension is absent or disabled.</summary>
    private static bool IsMissingInterface(DBusException ex)
        => ex.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod"
            or "org.freedesktop.DBus.Error.UnknownObject"
            or "org.freedesktop.DBus.Error.UnknownInterface";
}
