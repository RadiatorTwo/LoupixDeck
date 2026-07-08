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

    private static bool IsDeviceInitiatedEvent(byte command) =>
        command is (byte)Constants.Command.BUTTON_PRESS
            or (byte)Constants.Command.KNOB_ROTATE
            or (byte)Constants.Command.TOUCH
            or (byte)Constants.Command.TOUCH_END
            or (byte)Constants.Command.WHEEL_TOUCH
            or (byte)Constants.Command.WHEEL_TOUCH_END;
}
