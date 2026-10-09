using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Observes <c>NSWorkspaceScreensDidSleepNotification</c> / <c>NSWorkspaceScreensDidWakeNotification</c>
/// on the shared workspace's notification center. These are the display-sleep notifications,
/// separate from the system-sleep ones. AppKit wants an Objective-C object with selectors as the
/// observer, so a tiny class is registered with the runtime whose two methods call back into
/// managed code. Notifications are posted on the main run loop, which Avalonia drives.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe partial class MacDisplayStateService : DisplayStateServiceBase, IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string ObserverClassName = "LoupixDeckDisplayStateObserver";

    // The native methods are static, so they reach the (single, root-scoped) service through this.
    private static MacDisplayStateService s_instance;

    private IntPtr _observer;
    private IntPtr _center;

    protected override void Start()
    {
        s_instance = this;

        IntPtr sleepSelector = sel_registerName("screensDidSleep:");
        IntPtr wakeSelector = sel_registerName("screensDidWake:");

        IntPtr observerClass = objc_getClass(ObserverClassName);
        if (observerClass == IntPtr.Zero)
        {
            observerClass = objc_allocateClassPair(objc_getClass("NSObject"), ObserverClassName, 0);
            if (observerClass == IntPtr.Zero)
            {
                Console.WriteLine("[Displays] Could not create the macOS display observer class.");
                return;
            }

            // "v@:@" = void return, self, _cmd, one object argument (the NSNotification).
            class_addMethod(observerClass, sleepSelector,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScreensDidSleep, "v@:@");
            class_addMethod(observerClass, wakeSelector,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScreensDidWake, "v@:@");
            objc_registerClassPair(observerClass);
        }

        _observer = SendIntPtr(SendIntPtr(observerClass, sel_registerName("alloc")), sel_registerName("init"));
        if (_observer == IntPtr.Zero) return;

        IntPtr workspace = SendIntPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
        _center = SendIntPtr(workspace, sel_registerName("notificationCenter"));
        if (_center == IntPtr.Zero) return;

        IntPtr addObserver = sel_registerName("addObserver:selector:name:object:");
        AddObserver(_center, addObserver, _observer, sleepSelector,
            NsString("NSWorkspaceScreensDidSleepNotification"), IntPtr.Zero);
        AddObserver(_center, addObserver, _observer, wakeSelector,
            NsString("NSWorkspaceScreensDidWakeNotification"), IntPtr.Zero);

        MarkSupported();
    }

    [UnmanagedCallersOnly]
    private static void OnScreensDidSleep(IntPtr self, IntPtr selector, IntPtr notification)
    {
        // Native callback - an exception must never unwind into AppKit.
        try { s_instance?.Report(false); } catch { /* ignore */ }
    }

    [UnmanagedCallersOnly]
    private static void OnScreensDidWake(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try { s_instance?.Report(true); } catch { /* ignore */ }
    }

    private static IntPtr NsString(string value) =>
        StringWithUtf8String(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), value);

    public void Dispose()
    {
        if (_center != IntPtr.Zero && _observer != IntPtr.Zero)
            SendVoid(_center, sel_registerName("removeObserver:"), _observer);
        if (_observer != IntPtr.Zero)
            SendVoid(_observer, sel_registerName("release"));
        _observer = IntPtr.Zero;
        _center = IntPtr.Zero;
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [LibraryImport(ObjC)]
    private static partial void objc_registerClassPair(IntPtr cls);

    // The BOOL return is deliberately ignored: a bool return is not ABI-safe on arm64.
    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

    // objc_msgSend is declared once per message signature, as the ABI requires.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr StringWithUtf8String(IntPtr receiver, IntPtr selector, string value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void AddObserver(IntPtr receiver, IntPtr selector, IntPtr observer,
        IntPtr observerSelector, IntPtr name, IntPtr obj);
}
