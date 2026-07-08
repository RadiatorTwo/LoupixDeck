using System.Collections.Concurrent;
using System.Diagnostics;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Per-device accumulators for the display-transfer benchmark (spike, opt-in via
/// LOUPIXDECK_DISPLAY_BENCH=1). One instance per <see cref="LoupedeckDevice"/>, so running
/// the benchmark on several devices at once keeps each device's numbers isolated — unlike a
/// static/global accumulator, which would mix them.
///
/// Answers the key question: is the animation bottleneck the blocking serial write
/// (link/bandwidth-bound) or waiting on FRAMEBUFF/DRAW ACK round-trips (pipeline-bound)?
///
/// A single device only ever runs one benchmark window at a time; concurrency is only ever
/// across different devices (different instances), so per-instance fields need no locking
/// beyond the concurrent collections used for the cross-thread send/receive path.
/// </summary>
internal sealed class DisplayBenchmark(string deviceTag)
{
    private volatile bool _active;
    private long _startTicks;
    private long _payloadBytes; // application-layer payload bytes written
    private long _frameBytes;   // on-wire frame bytes (incl. WS masking/header overhead)
    private long _writeTicks;    // total time spent inside the blocking SerialPort.Write

    // Send timestamp per in-flight transaction id (per device, so tx-id spaces never collide
    // across devices). Ack samples are grouped per command (FRAMEBUFF, DRAW, ...).
    private readonly ConcurrentDictionary<byte, long> _sendTicks = new();
    private readonly ConcurrentDictionary<byte, List<double>> _ackMs = new();

    public string DeviceTag => deviceTag;
    public bool Active => _active;

    /// <summary>Starts a measurement window and resets all accumulators.</summary>
    public void Begin(string label)
    {
        _payloadBytes = 0;
        _frameBytes = 0;
        _writeTicks = 0;
        _sendTicks.Clear();
        _ackMs.Clear();
        _startTicks = Stopwatch.GetTimestamp();
        _active = true;
        Console.WriteLine($"[Bench][{deviceTag}] BEGIN {label}");
    }

    /// <summary>Write observer target — accumulates one completed transport write.</summary>
    public void RecordWrite(int payloadBytes, int frameBytes, TimeSpan writeDuration)
    {
        if (!_active) return;
        Interlocked.Add(ref _payloadBytes, payloadBytes);
        Interlocked.Add(ref _frameBytes, frameBytes);
        Interlocked.Add(ref _writeTicks, writeDuration.Ticks);
    }

    /// <summary>Records the moment a response-expecting command was queued for send.</summary>
    public void OnSent(byte transactionId)
    {
        if (_active) _sendTicks[transactionId] = Stopwatch.GetTimestamp();
    }

    /// <summary>Records a matched response, capturing its on-wire round-trip time.</summary>
    public void OnReceived(byte transactionId, byte command)
    {
        if (!_active) return;
        if (_sendTicks.TryRemove(transactionId, out var sendTicks))
            _ackMs.GetOrAdd(command, _ => []).Add(Stopwatch.GetElapsedTime(sendTicks).TotalMilliseconds);
    }

    /// <summary>Ends the window and prints the summary for <paramref name="frames"/> frames.</summary>
    public void End(string label, int frames)
    {
        var wallMs = Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds;
        _active = false;

        var payloadBytes = Interlocked.Read(ref _payloadBytes);
        var frameBytes = Interlocked.Read(ref _frameBytes);
        var writeMs = TimeSpan.FromTicks(Interlocked.Read(ref _writeTicks)).TotalMilliseconds;

        var seconds = wallMs / 1000.0;
        var fps = seconds > 0 ? frames / seconds : 0;
        var payloadMbps = seconds > 0 ? payloadBytes / 1024.0 / 1024.0 / seconds : 0;
        var wireMbps = seconds > 0 ? frameBytes / 1024.0 / 1024.0 / seconds : 0;
        var writePct = wallMs > 0 ? writeMs / wallMs * 100.0 : 0;

        Console.WriteLine($"[Bench][{deviceTag}] END {label}");
        Console.WriteLine($"[Bench][{deviceTag}]   frames={frames} wall={wallMs:F0}ms fps={fps:F1}");
        Console.WriteLine(
            $"[Bench][{deviceTag}]   payload={payloadBytes / 1024.0 / 1024.0:F2}MB ({payloadMbps:F2} MB/s)  " +
            $"onWire={frameBytes / 1024.0 / 1024.0:F2}MB ({wireMbps:F2} MB/s)");
        Console.WriteLine(
            $"[Bench][{deviceTag}]   write={writeMs:F0}ms ({writePct:F0}% of wall) -> " +
            $"{(writePct >= 50 ? "LINK/BANDWIDTH-bound" : "ACK/ROUND-TRIP-bound")}");

        foreach (var (command, samples) in _ackMs)
        {
            if (samples.Count == 0) continue;
            var sorted = samples.OrderBy(v => v).ToList();
            var avg = sorted.Average();
            var p95 = sorted[(int)Math.Ceiling(0.95 * (sorted.Count - 1))];
            var name = Enum.IsDefined(typeof(Constants.Command), command)
                ? ((Constants.Command)command).ToString()
                : $"0x{command:x2}";
            Console.WriteLine(
                $"[Bench][{deviceTag}]   {name,-10} ack avg={avg:F1}ms p95={p95:F1}ms (n={sorted.Count})");
        }
    }
}
