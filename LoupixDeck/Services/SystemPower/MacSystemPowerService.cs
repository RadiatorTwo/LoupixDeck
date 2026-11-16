using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// System sleep and wake on macOS (issue #522), from <c>NSWorkspaceWillSleepNotification</c> and
/// <c>NSWorkspaceDidWakeNotification</c> on the shared workspace's notification center. Without
/// it the device was never blanked when the Mac went to sleep. The wake is also caught by the
/// wall-clock check in <see cref="ResumeDetectingSystemPowerService"/>, which reports it once.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacSystemPowerService : ISystemPowerService, IDisposable
{
    // The native methods are static, so they reach the (single, root-scoped) service through this.
    private static MacSystemPowerService s_instance;

    private MacNotificationObserver _observer;
    private bool _started;

    public event EventHandler Suspending;
    public event EventHandler Resuming;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;
        s_instance = this;

        try
        {
            _observer = MacNotificationObserver.Attach("LoupixDeckSystemPowerObserver",
                MacNotificationObserver.WorkspaceCenter(), distributed: false,
                new MacNotificationObserver.Handler("willSleep:", "NSWorkspaceWillSleepNotification",
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnWillSleep),
                new MacNotificationObserver.Handler("didWake:", "NSWorkspaceDidWakeNotification",
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnDidWake));

            if (_observer == null)
                Console.WriteLine("[Power] Could not attach the macOS sleep/wake observer.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Power] macOS sleep/wake monitoring unavailable: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWillSleep(IntPtr self, IntPtr selector, IntPtr notification)
    {
        // Native callback - an exception must never unwind into AppKit.
        try
        {
            var instance = s_instance;
            if (instance == null) return;
            Console.WriteLine($"[Power] Sleep reported by the OS; " +
                              $"{instance.Suspending?.GetInvocationList().Length ?? 0} handler(s).");
            instance.Suspending?.Invoke(instance, EventArgs.Empty);
        }
        catch { /* ignore */ }
    }

    [UnmanagedCallersOnly]
    private static void OnDidWake(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            var instance = s_instance;
            instance?.Resuming?.Invoke(instance, EventArgs.Empty);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _observer?.Dispose();
        _observer = null;
        if (ReferenceEquals(s_instance, this)) s_instance = null;
    }
}
