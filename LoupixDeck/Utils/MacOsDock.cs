using System.Runtime.InteropServices;

namespace LoupixDeck.Utils;

/// <summary>
/// Switches the process between a regular app (Dock icon and menu bar) and an accessory app
/// (menu-bar status item only) through <c>NSApplication.setActivationPolicy:</c>. The Dock icon
/// is only wanted while the main window is up. A no-op on every other platform.
/// </summary>
/// <remarks>
/// Avalonia only exposes <c>MacOSPlatformOptions.ShowInDock</c> at startup, hence the Objective-C
/// runtime calls. A policy switch is the thing that can leave AppKit with a stale menu bar, so
/// the current policy is read first and the call skipped when nothing changes.
/// </remarks>
public static partial class MacOsDock
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    // NSApplicationActivationPolicy
    private const long NSApplicationActivationPolicyRegular = 0;
    private const long NSApplicationActivationPolicyAccessory = 1;

    /// <summary>Whether this platform has a Dock at all.</summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>Dock icon and app menu bar on. Call before showing the window.</summary>
    public static void Show() => SetActivationPolicy(NSApplicationActivationPolicyRegular);

    /// <summary>Status item only. Call after every window has been hidden.</summary>
    public static void Hide() => SetActivationPolicy(NSApplicationActivationPolicyAccessory);

    private static void SetActivationPolicy(long policy)
    {
        if (!IsSupported) return;

        try
        {
            IntPtr app = SharedApplication(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;

            if (ActivationPolicy(app, sel_registerName("activationPolicy")) == policy) return;

            SetActivationPolicy(app, sel_registerName("setActivationPolicy:"), policy);
        }
        catch (Exception ex)
        {
            // A binding failure must never take the app down.
            Console.WriteLine($"[MacOsDock] Could not set activation policy {policy}: {ex.Message}");
        }
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SharedApplication(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial long ActivationPolicy(IntPtr receiver, IntPtr selector);

    // The ObjC BOOL return is deliberately ignored: a bool return is not ABI-safe on arm64.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SetActivationPolicy(IntPtr receiver, IntPtr selector, long policy);
}
