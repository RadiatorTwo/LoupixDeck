using System.Collections.Concurrent;
using System.Diagnostics;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Opt-in, self-contained diagnostics for the device send/receive path (issue #149).
/// Deliberately kept OUT of the production send logic: when disabled (the default), the
/// production code only ever evaluates the cheap <see cref="Enabled"/> flag and declares
/// no diagnostic state of its own. All timing state and formatting live here.
///
/// Enable with environment variable LOUPIXDECK_DEBUG_TIMING=1 (logs anomalies: orphaned
/// responses, missing/real timeouts, framing resyncs, plus responses slower than
/// <see cref="SlowThreshold"/>). Add LOUPIXDECK_DEBUG_TIMING_VERBOSE=1 to log every
/// response, not just the slow ones.
/// </summary>
internal static class SendDiagnostics
{
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("LOUPIXDECK_DEBUG_TIMING") == "1";

    /// <summary>
    /// True when the send/receive path should capture timing: either the user enabled
    /// timing via the env var, or a display benchmark run is currently active. The
    /// benchmark (spike) needs the same OnSent/OnReceived/OnBytesWritten hooks live even
    /// when <see cref="Enabled"/> is false, so callers gate on this instead of Enabled.
    /// </summary>
    public static bool CaptureActive => Enabled || _benchActive;

    private static readonly bool Verbose =
        Environment.GetEnvironmentVariable("LOUPIXDECK_DEBUG_TIMING_VERBOSE") == "1";

    // Responses normally answer in 0-2ms; anything beyond this is worth seeing even
    // without verbose mode, as it is a near-miss for the send timeout.
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(500);

    // Send timestamp per in-flight transaction id, used to measure on-wire round-trip
    // time. Keyed by the 1-byte transaction id, so it is naturally bounded to 256
    // entries and self-overwrites on id reuse; a timed-out transaction's stale entry is
    // reclaimed the next time that id comes around.
    private static readonly ConcurrentDictionary<byte, long> SendTicks = new();

    /// <summary>Records the moment a response-expecting command was written to the device.</summary>
    public static void OnSent(byte transactionId) => SendTicks[transactionId] = Stopwatch.GetTimestamp();

    /// <summary>
    /// Records a received frame. For a matched response, logs the on-wire time (only if
    /// slow, unless verbose). For an unmatched command-response, logs an ORPHAN — the
    /// #149 smoking gun: either the command already timed out (a late answer) or the
    /// stream desynced and we mis-read the id. Device-initiated events (button/touch/
    /// rotate) carry ids we never issued and are ignored.
    /// </summary>
    public static void OnReceived(byte transactionId, byte command, bool matched)
    {
        if (SendTicks.TryRemove(transactionId, out var sendTicks))
        {
            var onWire = Stopwatch.GetElapsedTime(sendTicks);
            if (_benchActive)
                RecordAck(command, onWire.TotalMilliseconds);
            if (Verbose || onWire >= SlowThreshold)
                Console.WriteLine(
                    $"[Timing] response tx={transactionId} cmd=0x{command:x2} answered in {onWire.TotalMilliseconds:F0}ms on the wire");
        }
        else if (!matched && !IsDeviceInitiatedEvent(command))
        {
            Console.WriteLine(
                $"[Timing][ORPHAN] response tx={transactionId} cmd=0x{command:x2} had no pending transaction (late or desynced)");
        }
    }

    /// <summary>
    /// Logs a command whose response did not arrive before its timeout. A benign timeout
    /// (a missing DRAW ACK that we tolerate) is tagged differently from a real one.
    /// </summary>
    public static void OnTimeout(Constants.Command command, bool benign) =>
        Console.WriteLine($"[Timing][{(benign ? "MISSING ACK" : "REAL TIMEOUT")}] {command} (no response within timeout)");

    /// <summary>Logs an abnormal serial-framing event (stream resync, discard, undecoded length).</summary>
    public static void OnFraming(string message) => Console.WriteLine($"[Framing] {message}");

    // ---------------------------------------------------------------------------------
    // Display-transfer benchmark (spike). Measures whether the animation bottleneck is the
    // blocking serial Write (link/bandwidth-bound) or the FRAMEBUFF/DRAW ACK round-trips
    // (pipeline-bound). All state lives here; only active while a benchmark run is in flight.
    // ---------------------------------------------------------------------------------

    private static volatile bool _benchActive;
    private static long _benchStartTicks;
    private static long _benchPayloadBytes; // sum of application-layer payload bytes written
    private static long _benchFrameBytes;   // sum of on-wire frame bytes (incl. WS masking overhead)
    private static long _benchWriteTicks;    // total time spent inside the blocking SerialPort.Write
    private static readonly ConcurrentDictionary<byte, List<double>> BenchAckMs = new();

    /// <summary>Starts a benchmark window: resets accumulators and enables capture.</summary>
    public static void BeginBenchmark(string label)
    {
        _benchPayloadBytes = 0;
        _benchFrameBytes = 0;
        _benchWriteTicks = 0;
        BenchAckMs.Clear();
        _benchStartTicks = Stopwatch.GetTimestamp();
        _benchActive = true;
        Console.WriteLine($"[Bench] BEGIN {label}");
    }

    /// <summary>
    /// Records a completed write on the transport. Called from the serial Send path when
    /// <see cref="CaptureActive"/>; accumulates only while a benchmark window is active.
    /// </summary>
    public static void OnBytesWritten(int payloadBytes, int frameBytes, TimeSpan writeDuration)
    {
        if (!_benchActive) return;
        Interlocked.Add(ref _benchPayloadBytes, payloadBytes);
        Interlocked.Add(ref _benchFrameBytes, frameBytes);
        Interlocked.Add(ref _benchWriteTicks, writeDuration.Ticks);
    }

    private static void RecordAck(byte command, double ms) =>
        BenchAckMs.GetOrAdd(command, _ => new List<double>()).Add(ms);

    /// <summary>Ends the benchmark window and prints the summary for <paramref name="frames"/> frames.</summary>
    public static void EndBenchmark(string label, int frames)
    {
        var wall = Stopwatch.GetElapsedTime(_benchStartTicks);
        _benchActive = false;

        var payloadBytes = Interlocked.Read(ref _benchPayloadBytes);
        var frameBytes = Interlocked.Read(ref _benchFrameBytes);
        var writeMs = TimeSpan.FromTicks(Interlocked.Read(ref _benchWriteTicks)).TotalMilliseconds;
        var wallMs = wall.TotalMilliseconds;

        var fps = wallMs > 0 ? frames / (wallMs / 1000.0) : 0;
        var payloadMbps = wallMs > 0 ? payloadBytes / 1024.0 / 1024.0 / (wallMs / 1000.0) : 0;
        var wireMbps = wallMs > 0 ? frameBytes / 1024.0 / 1024.0 / (wallMs / 1000.0) : 0;
        var writePct = wallMs > 0 ? writeMs / wallMs * 100.0 : 0;

        Console.WriteLine($"[Bench] END {label}");
        Console.WriteLine(
            $"[Bench]   frames={frames} wall={wallMs:F0}ms fps={fps:F1}");
        Console.WriteLine(
            $"[Bench]   payload={payloadBytes / 1024.0 / 1024.0:F2}MB ({payloadMbps:F2} MB/s)  " +
            $"onWire={frameBytes / 1024.0 / 1024.0:F2}MB ({wireMbps:F2} MB/s)");
        Console.WriteLine(
            $"[Bench]   write={writeMs:F0}ms ({writePct:F0}% of wall) -> " +
            $"{(writePct >= 50 ? "LINK/BANDWIDTH-bound" : "ACK/ROUND-TRIP-bound")}");

        foreach (var (command, samples) in BenchAckMs)
        {
            if (samples.Count == 0) continue;
            var sorted = samples.OrderBy(v => v).ToList();
            var avg = sorted.Average();
            var p95 = sorted[(int)Math.Ceiling(0.95 * (sorted.Count - 1))];
            var name = Enum.IsDefined(typeof(Constants.Command), command)
                ? ((Constants.Command)command).ToString()
                : $"0x{command:x2}";
            Console.WriteLine(
                $"[Bench]   {name,-10} ack avg={avg:F1}ms p95={p95:F1}ms (n={sorted.Count})");
        }
    }

    private static bool IsDeviceInitiatedEvent(byte command) =>
        command is (byte)Constants.Command.BUTTON_PRESS
            or (byte)Constants.Command.KNOB_ROTATE
            or (byte)Constants.Command.TOUCH
            or (byte)Constants.Command.TOUCH_END
            or (byte)Constants.Command.WHEEL_TOUCH
            or (byte)Constants.Command.WHEEL_TOUCH_END;
}
