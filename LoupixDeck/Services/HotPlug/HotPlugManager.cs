using LoupixDeck.Registry;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.HotPlug;

/// <inheritdoc cref="IHotPlugManager"/>
public sealed class HotPlugManager : IHotPlugManager
{
    // USB events arrive in bursts (a single plug raises several); collapse them.
    private const int DebounceMs = 750;

    // While a device is missing but not yet confirmed gone, re-run reconcile on this
    // cadence so a SINGLE removal event still reaches the confirmation threshold (the
    // watcher won't keep firing on its own).
    private const int ReArmMs = 600;

    // A present-but-disconnected device (its port is held by another program) produces no USB
    // event when the port finally frees, so keep re-running reconcile on this slower cadence to
    // retry the reconnect until it succeeds. Slower than ReArmMs because a device scan is not
    // free and this can run for as long as the other program keeps the port.
    private const int ReviveMs = 3000;

    // A device must be missing from this many consecutive scans before we detach
    // it. Guards against a single failed/raced USB scan tearing a live device down
    // (and smooths a quick unplug→replug, which the device's own auto-reconnect
    // already recovers without any host churn).
    private const int DetachConfirmScans = 2;

    private readonly IDeviceWatcher _watcher;
    private readonly IDeviceHostRegistry _registry;

    private readonly Lock _gate = new();
    private readonly Timer _debounce;
    private readonly Dictionary<string, int> _missCounts = new(StringComparer.OrdinalIgnoreCase);
    // Keys for which a detach was already raised, kept until the host actually leaves
    // the registry (App removes it async on the UI thread) so we never double-detach.
    private readonly HashSet<string> _detaching = new(StringComparer.OrdinalIgnoreCase);
    private bool _reconciling;

    public event Action<ResolvedDevice> DeviceAttached;
    public event Action<DeviceHost> DeviceDetached;

    public HotPlugManager(IDeviceWatcher watcher, IDeviceHostRegistry registry)
    {
        _watcher = watcher;
        _registry = registry;
        _debounce = new Timer(_ => Reconcile(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _watcher.DevicesChanged += () => ScheduleReconcile(DebounceMs);
        _watcher.Start();

        // The app exits via Environment.Exit(0) (tray/window close), which skips
        // normal disposal. Drop the watcher's native subscription explicitly on
        // process exit so it can't linger past our lifetime.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { _watcher.Dispose(); } catch { /* best effort */ }
        };
    }

    private void ScheduleReconcile(int delayMs)
    {
        try { _debounce.Change(delayMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    private void Reconcile()
    {
        // Never let two reconciles overlap — a slow scan could otherwise double-fire.
        lock (_gate)
        {
            if (_reconciling) return;
            _reconciling = true;
        }

        try
        {
            List<ResolvedDevice> scan;
            try
            {
                scan = ActiveDeviceResolver.ResolveAll();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HotPlug] scan failed: {ex.Message}");
                return;
            }

            var scanKeys = new HashSet<string>(scan.Select(d => d.ScopeKey), StringComparer.OrdinalIgnoreCase);
            var hosts = _registry.Hosts;
            var hostKeys = new HashSet<string>(hosts.Select(h => h.Device.ScopeKey), StringComparer.OrdinalIgnoreCase);

            Console.WriteLine($"[HotPlug] reconcile: {scan.Count} connected, {hosts.Count} running");

            // Forget detach-in-progress markers once the host has actually been removed.
            _detaching.RemoveWhere(k => !hostKeys.Contains(k));

            // ── Detach: a running host whose device is no longer present. ──
            // Skip entirely while the DEBUG fake-device override is active: the scan
            // can legitimately be empty (no real hardware) even though a fake device
            // was brought up via the offline path, and we must not tear that down.
            var fakeActive = FakeDeviceActive();
            foreach (var host in hosts)
            {
                var key = host.Device.ScopeKey;
                if (scanKeys.Contains(key))
                {
                    _missCounts.Remove(key);
                    continue;
                }

                if (fakeActive || _detaching.Contains(key))
                    continue;

                var misses = _missCounts.GetValueOrDefault(key) + 1;
                if (misses < DetachConfirmScans)
                {
                    _missCounts[key] = misses;
                    continue;
                }

                _missCounts.Remove(key);
                _detaching.Add(key);
                Console.WriteLine($"[HotPlug] device gone: {host.Device.Info.Name} ({key})");
                Raise(DeviceDetached, host);
            }

            // ── Attach: a connected device with no running host. ──
            foreach (var device in scan)
            {
                if (hostKeys.Contains(device.ScopeKey))
                    continue;
                Console.WriteLine($"[HotPlug] device appeared: {device.Info.Name} ({device.ScopeKey})");
                Raise(DeviceAttached, device);
            }

            // ── Revive: a present device whose serial link is down. ──
            // The device is on the bus but its port was taken (e.g. the vendor's own Loupedeck
            // app grabbed it), so its host stays up with a dead connection. Presence alone never
            // triggers a reconnect — the attach loop skips it (a host already exists) and freeing
            // the port raises no USB event. Kick a reconnect from here so that the moment the port
            // frees we take it over, without a restart and without killing anything. RequestReconnect
            // is non-blocking and self-throttling (it complements the device's own reconnect loop).
            var reviving = false;
            foreach (var host in hosts)
            {
                if (!scanKeys.Contains(host.Device.ScopeKey)) continue; // gone — handled by detach
                if (host.Controller.IsDeviceConnected) continue;        // already live
                reviving = true;
                host.Controller.RequestReconnect();
            }

            // Self-re-arm: while a removal is still being confirmed or a detach is pending host
            // removal (ReArmMs), or while a present device is still being revived (the slower
            // ReviveMs), so recovery never depends on another external event.
            if (_missCounts.Count > 0 || _detaching.Count > 0)
                ScheduleReconcile(ReArmMs);
            else if (reviving)
                ScheduleReconcile(ReviveMs);
        }
        finally
        {
            lock (_gate) _reconciling = false;
        }
    }

    private static bool FakeDeviceActive()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LOUPIXDECK_FAKE_DEVICE"));

    private static void Raise<T>(Action<T> handler, T arg)
    {
        try { handler?.Invoke(arg); }
        catch (Exception ex) { Console.WriteLine($"[HotPlug] handler threw: {ex.Message}"); }
    }
}
