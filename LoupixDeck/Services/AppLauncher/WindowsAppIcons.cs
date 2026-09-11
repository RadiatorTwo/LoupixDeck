#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Pulls the application icon out of a Windows executable and writes it as a PNG.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsAppIcons
{
    /// <summary>
    /// Requested icon size. Modern executables carry a 256x256 variant, and asking for it is the
    /// difference between a crisp deck button and a blurry upscale of the 32px icon that
    /// <see cref="Icon.ExtractAssociatedIcon"/> is limited to.
    /// </summary>
    private const int PreferredSize = 256;

    public static bool TryExtractPng(string executable, string destination)
        => TryExtractPreferred(executable, destination) || TryExtractAssociated(executable, destination);

    private static bool TryExtractPreferred(string executable, string destination)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            int extracted = PrivateExtractIcons(executable, 0, PreferredSize, PreferredSize,
                out icon, IntPtr.Zero, 1, 0);

            if (extracted <= 0 || icon == IntPtr.Zero)
                return false;

            Save(icon, destination);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"[AppIcon] Cannot extract a large icon from '{executable}': {ex.Message}");
            return false;
        }
        finally
        {
            // Icon.FromHandle does not take ownership of the HICON, so the handle has to be
            // released explicitly — on the success path too.
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
        }
    }

    private static bool TryExtractAssociated(string executable, string destination)
    {
        try
        {
            using Icon icon = Icon.ExtractAssociatedIcon(executable);
            if (icon == null)
                return false;

            using Bitmap bitmap = icon.ToBitmap();
            bitmap.Save(destination, ImageFormat.Png);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"[AppIcon] Cannot extract an icon from '{executable}': {ex.Message}");
            return false;
        }
    }

    private static void Save(IntPtr iconHandle, string destination)
    {
        using Icon icon = Icon.FromHandle(iconHandle);
        using Bitmap bitmap = icon.ToBitmap();
        bitmap.Save(destination, ImageFormat.Png);
    }

    [LibraryImport("user32.dll", EntryPoint = "PrivateExtractIconsW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PrivateExtractIcons(string fileName, int iconIndex, int cx, int cy,
        out IntPtr icon, IntPtr iconId, int iconCount, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);
}
#endif