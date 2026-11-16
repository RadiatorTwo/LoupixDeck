using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Session lock state on macOS (issue #522), from two sources:
/// <list type="bullet">
/// <item><c>com.apple.screenIsLocked</c> / <c>com.apple.screenIsUnlocked</c> on the distributed
/// notification center: the lock screen, whether from a manual lock, the screen saver or a wake
/// that asks for the password. These are not documented by Apple, though widely relied on.</item>
/// <item><c>NSWorkspaceSessionDidResignActiveNotification</c> /
/// <c>NSWorkspaceSessionDidBecomeActiveNotification</c>: fast user switching to another account
/// and back.</item>
/// </list>
/// The session counts as locked while either says so.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacSessionLockService : SessionLockServiceBase, IDisposable
{
    // The native methods are static, so they reach the (single, root-scoped) service through this.
    private static MacSessionLockService s_instance;

    private MacNotificationObserver _screenObserver;
    private MacNotificationObserver _sessionObserver;

    private volatile bool _screenLocked;
    private volatile bool _sessionInactive;

    protected override void Start()
    {
        s_instance = this;

        _screenObserver = MacNotificationObserver.Attach("LoupixDeckScreenLockObserver",
            MacNotificationObserver.DistributedCenter(), distributed: true,
            new MacNotificationObserver.Handler("screenIsLocked:", "com.apple.screenIsLocked",
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScreenLocked),
            new MacNotificationObserver.Handler("screenIsUnlocked:", "com.apple.screenIsUnlocked",
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnScreenUnlocked));

        _sessionObserver = MacNotificationObserver.Attach("LoupixDeckSessionActiveObserver",
            MacNotificationObserver.WorkspaceCenter(), distributed: false,
            new MacNotificationObserver.Handler("sessionDidResignActive:", "NSWorkspaceSessionDidResignActiveNotification",
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnSessionResignedActive),
            new MacNotificationObserver.Handler("sessionDidBecomeActive:", "NSWorkspaceSessionDidBecomeActiveNotification",
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnSessionBecameActive));

        if (_screenObserver == null)
            Console.WriteLine("[Lock] Could not attach the macOS screen lock observer.");
        if (_sessionObserver == null)
            Console.WriteLine("[Lock] Could not attach the macOS session switch observer.");

        if (_screenObserver != null || _sessionObserver != null)
            MarkSupported();
    }

    private void Update(string source) => Report(_screenLocked || _sessionInactive, source);

    [UnmanagedCallersOnly]
    private static void OnScreenLocked(IntPtr self, IntPtr selector, IntPtr notification)
    {
        // Native callback - an exception must never unwind into AppKit.
        try
        {
            if (s_instance is not { } instance) return;
            instance._screenLocked = true;
            instance.Update("screen locked");
        }
        catch { /* ignore */ }
    }

    [UnmanagedCallersOnly]
    private static void OnScreenUnlocked(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            if (s_instance is not { } instance) return;
            instance._screenLocked = false;
            instance.Update("screen unlocked");
        }
        catch { /* ignore */ }
    }

    [UnmanagedCallersOnly]
    private static void OnSessionResignedActive(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            if (s_instance is not { } instance) return;
            instance._sessionInactive = true;
            instance.Update("switched to another user");
        }
        catch { /* ignore */ }
    }

    [UnmanagedCallersOnly]
    private static void OnSessionBecameActive(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            if (s_instance is not { } instance) return;
            instance._sessionInactive = false;
            instance.Update("switched back to this user");
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _screenObserver?.Dispose();
        _sessionObserver?.Dispose();
        _screenObserver = null;
        _sessionObserver = null;
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }
}
