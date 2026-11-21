using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// An Objective-C observer for AppKit / Foundation notifications. AppKit wants an object with
/// selectors as the observer, so a small class is registered with the runtime whose methods call
/// straight into the unmanaged callbacks passed in. Notifications are posted on the main run
/// loop, which Avalonia drives. Same approach as <see cref="MacDisplayStateService"/>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacNotificationObserver : IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary><c>NSNotificationSuspensionBehaviorDeliverImmediately</c>. A Cocoa app suspends
    /// distributed notifications while it is not the active app, which for a tray app is nearly
    /// always; this behaviour delivers them regardless.</summary>
    private const nuint DeliverImmediately = 4;

    /// <summary>One notification and the callback it goes to. The callback has the signature
    /// <c>void (id self, SEL _cmd, NSNotification *note)</c>.</summary>
    public readonly record struct Handler(string Selector, string Notification, IntPtr Callback);

    private IntPtr _observer;
    private IntPtr _center;

    private MacNotificationObserver(IntPtr observer, IntPtr center)
    {
        _observer = observer;
        _center = center;
    }

    /// <summary>The shared workspace's notification center (sleep, wake, session switches).</summary>
    public static IntPtr WorkspaceCenter()
    {
        IntPtr workspace = SendIntPtr(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
        return workspace == IntPtr.Zero ? IntPtr.Zero : SendIntPtr(workspace, sel_registerName("notificationCenter"));
    }

    /// <summary>The system-wide distributed notification center (screen lock and unlock).</summary>
    public static IntPtr DistributedCenter() =>
        SendIntPtr(objc_getClass("NSDistributedNotificationCenter"), sel_registerName("defaultCenter"));

    /// <summary>
    /// Registers the observer class (once per name) and attaches an instance to
    /// <paramref name="center"/> for every handler. Returns null when the runtime refuses.
    /// </summary>
    public static MacNotificationObserver Attach(string className, IntPtr center, bool distributed,
        params Handler[] handlers)
    {
        if (center == IntPtr.Zero) return null;

        IntPtr observerClass = objc_getClass(className);
        if (observerClass == IntPtr.Zero)
        {
            observerClass = objc_allocateClassPair(objc_getClass("NSObject"), className, 0);
            if (observerClass == IntPtr.Zero) return null;

            // "v@:@" = void return, self, _cmd, one object argument (the NSNotification).
            foreach (var handler in handlers)
                class_addMethod(observerClass, sel_registerName(handler.Selector), handler.Callback, "v@:@");
            objc_registerClassPair(observerClass);
        }

        IntPtr observer = SendIntPtr(SendIntPtr(observerClass, sel_registerName("alloc")), sel_registerName("init"));
        if (observer == IntPtr.Zero) return null;

        foreach (var handler in handlers)
        {
            IntPtr selector = sel_registerName(handler.Selector);
            IntPtr name = NsString(handler.Notification);
            if (distributed)
                AddObserver(center, sel_registerName("addObserver:selector:name:object:suspensionBehavior:"),
                    observer, selector, name, IntPtr.Zero, DeliverImmediately);
            else
                AddObserver(center, sel_registerName("addObserver:selector:name:object:"),
                    observer, selector, name, IntPtr.Zero);
        }

        return new MacNotificationObserver(observer, center);
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

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void AddObserver(IntPtr receiver, IntPtr selector, IntPtr observer,
        IntPtr observerSelector, IntPtr name, IntPtr obj, nuint suspensionBehavior);
}
