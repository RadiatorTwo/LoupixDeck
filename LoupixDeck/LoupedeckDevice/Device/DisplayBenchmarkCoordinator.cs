namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Orchestrates the opt-in display-transfer benchmark (spike) across all connected devices
/// so results are comparable and uncontended:
///   Phase 1 — each device on its own, sequentially (isolates one device's true throughput).
///   Phase 2 — all devices at once (shows contention / shared-host effects).
///
/// Devices register themselves on connect (gated by LOUPIXDECK_DISPLAY_BENCH=1). Because we
/// do not know up front how many devices will connect, the start is debounced: the run fires
/// once, <see cref="SettleMs"/> after the last registration, so every device that comes up in
/// that window is included in a single coordinated run.
/// </summary>
internal static class DisplayBenchmarkCoordinator
{
    private const int SettleMs = 4000;

    private static readonly object Gate = new();
    private static readonly List<LoupedeckDevice> Devices = [];
    private static Timer _debounce;
    private static bool _started;

    /// <summary>Registers a device for the coordinated run and (re)arms the debounce timer.</summary>
    public static void Register(LoupedeckDevice device)
    {
        lock (Gate)
        {
            if (_started) return;
            if (!Devices.Contains(device)) Devices.Add(device);
            _debounce?.Dispose();
            _debounce = new Timer(_ => Start(), null, SettleMs, Timeout.Infinite);
        }
    }

    private static void Start()
    {
        LoupedeckDevice[] devices;
        lock (Gate)
        {
            if (_started) return;
            _started = true;
            _debounce?.Dispose();
            _debounce = null;
            devices = Devices.ToArray();
        }

        _ = Task.Run(() => RunAsync(devices));
    }

    private static async Task RunAsync(LoupedeckDevice[] devices)
    {
        try
        {
            Console.WriteLine($"[Bench] ===== Display-transfer benchmark: {devices.Length} device(s) =====");

            // Phase 1: one device at a time, so nothing else contends for the USB host.
            Console.WriteLine("[Bench] --- Phase 1: individual devices (sequential) ---");
            foreach (var device in devices)
                await device.RunDisplayBenchmarkAsync("solo");

            // Phase 2: all devices concurrently — surfaces shared-host contention.
            Console.WriteLine("[Bench] --- Phase 2: all devices concurrently ---");
            await Task.WhenAll(devices.Select(device => device.RunDisplayBenchmarkAsync("parallel")));

            Console.WriteLine("[Bench] ===== Display-transfer benchmark complete =====");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bench] Coordinator run failed: {ex.Message}");
        }
    }
}
