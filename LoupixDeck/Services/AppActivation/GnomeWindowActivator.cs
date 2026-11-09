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
/// against both halves of WM_CLASS; for a native Wayland window both are its app_id. Activating a
/// window that already has focus changes nothing, so that case needs no detection.
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

    public static async Task<Result> ActivateAsync(IReadOnlyCollection<string> windowClasses,
        CancellationToken cancellationToken)
    {
        using Connection connection = await GnomeActivationExtension.ConnectAsync(cancellationToken);
        if (connection == null)
            return Result.Unavailable;

        IActivateWindowByTitle extension = connection.CreateProxy<IActivateWindowByTitle>(
            GnomeActivationExtension.ShellService, GnomeActivationExtension.ExtensionPath);

        try
        {
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

        return Result.NotFound;
    }

    /// <summary>The object or method is not exported: the extension is absent or disabled.</summary>
    private static bool IsMissingInterface(DBusException ex)
        => ex.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod"
            or "org.freedesktop.DBus.Error.UnknownObject"
            or "org.freedesktop.DBus.Error.UnknownInterface";
}
