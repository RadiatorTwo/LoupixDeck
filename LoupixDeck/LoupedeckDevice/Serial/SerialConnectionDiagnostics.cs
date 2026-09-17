using System.Collections.Concurrent;

namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>Why an attempt to open a deck's serial port ended the way it did.</summary>
public enum SerialAttemptOutcome
{
    /// <summary>The port opened and the handshake succeeded.</summary>
    Opened,

    /// <summary>The port exists but could not be opened: no permission, or another program has it.</summary>
    Denied,

    /// <summary>The port was not there at all - unplugged, or re-enumerated under another name.</summary>
    Missing,

    /// <summary>The port opened, but the device did not answer the handshake.</summary>
    HandshakeFailed,

    /// <summary>Anything else. <see cref="SerialAttempt.Detail"/> carries the raw message.</summary>
    Other
}

/// <summary>
/// The last open attempt on one port: what happened, when, and the raw message behind it.
/// </summary>
public sealed record SerialAttempt(
    string Port,
    int BaudRate,
    SerialAttemptOutcome Outcome,
    string Detail,
    DateTimeOffset At);

/// <summary>
/// Remembers how each deck's port was last opened, so diagnostics can report the real cause
/// instead of "not connected" (issue #258 phase 2).
///
/// Static and keyed by port for the same reason the failure log above it is: a
/// <see cref="SerialConnection"/> is built fresh for every attempt, so nothing on the instance
/// survives the failure it is supposed to explain. It holds one small record per port and never
/// grows beyond the number of ports that were tried.
/// </summary>
public static class SerialConnectionDiagnostics
{
    private static readonly ConcurrentDictionary<string, SerialAttempt> Attempts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last attempt on <paramref name="port"/>, or null when it was never tried.</summary>
    public static SerialAttempt For(string port)
        => (port != null) && Attempts.TryGetValue(port, out SerialAttempt attempt) ? attempt : null;

    /// <summary>Every port that was tried this session.</summary>
    public static IReadOnlyCollection<SerialAttempt> All() => Attempts.Values.ToList();

    /// <summary>Records a successful open.</summary>
    public static void RecordOpened(string port, int baudRate)
        => Record(port, new SerialAttempt(port, baudRate, SerialAttemptOutcome.Opened, null,
            DateTimeOffset.Now));

    /// <summary>Records a failed open, classified by the exception the attempt ended with.</summary>
    public static void RecordFailure(string port, int baudRate, Exception exception)
    {
        SerialAttemptOutcome outcome = exception switch
        {
            UnauthorizedAccessException => SerialAttemptOutcome.Denied,
            FileNotFoundException => SerialAttemptOutcome.Missing,
            // The handshake is the only IOException this code raises itself.
            IOException io when io.Message.Contains("Handshake", StringComparison.OrdinalIgnoreCase)
                => SerialAttemptOutcome.HandshakeFailed,
            IOException => SerialAttemptOutcome.Denied,
            _ => SerialAttemptOutcome.Other
        };

        Record(port, new SerialAttempt(port, baudRate, outcome,
            $"{exception.GetType().Name}: {exception.Message}", DateTimeOffset.Now));
    }

    private static void Record(string port, SerialAttempt attempt)
    {
        if (!string.IsNullOrEmpty(port))
        {
            Attempts[port] = attempt;
        }
    }
}
