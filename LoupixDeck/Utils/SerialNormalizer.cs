#nullable enable
using System.Collections.Frozen;
using System.Text;

namespace LoupixDeck.Utils;

/// <summary>
/// Normalizes USB iSerialNumber values into a single, platform-uniform identity
/// token and into a filesystem-safe form for per-device config-file scoping.
/// </summary>
/// <remarks>
/// Windows exposes the serial as the 3rd '\'-segment of the PNPDeviceID. For
/// devices with a real iSerial that segment is hex-encoded ASCII (e.g. "525A32…"
/// → "RZ2…"); for devices WITHOUT a real iSerial Windows synthesizes a
/// location-derived id containing '&amp;' (e.g. "6&amp;1a2b3c&amp;0&amp;2") which must NOT be
/// treated as a stable serial (it changes per USB port). Linux already yields
/// decoded ASCII via ID_SERIAL_SHORT, so only the Windows segment needs decoding.
/// </remarks>
public static class SerialNormalizer
{
    private const int MaxFilenameLength = 48;

    /// <summary>
    /// Decode a Windows PNPDeviceID instance segment into the device's real serial.
    /// Returns null when the segment is a Windows-synthesized location id (contains
    /// '&amp;') or is empty. Hex-encoded ASCII is decoded; anything else is returned
    /// unchanged.
    /// </summary>
    public static string? NormalizeWindowsPnpSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return null;
        segment = segment.Trim();

        // '&' marks a Windows-generated, port-location-dependent id, not a real iSerial.
        if (segment.Contains('&')) return null;

        return TryDecodeHexAscii(segment) ?? segment;
    }

    private static readonly FrozenSet<char> InvalidFileNameChars = Path.GetInvalidFileNameChars().ToFrozenSet();

    /// <summary>
    /// Map an identity serial onto a filesystem-safe token for config filenames.
    /// Invalid path chars, whitespace and our own '_' separator collapse to '-';
    /// the result is lowercased and length-capped.
    /// </summary>
    /// <returns>
    /// A filesystem-safe slug for the serial, or null if the input is empty or yields no valid characters: never let a raw serial reach a path.
    /// </returns>
    public static string? ForFilename(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return null;

        var sb = new StringBuilder(serial.Length);
        var lastDash = false;
        foreach (var ch in serial.Trim())
        {
            var bad = ch == '_' || char.IsWhiteSpace(ch) || InvalidFileNameChars.Contains(ch);
            if (bad)
            {
                if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        if (sb.Length > MaxFilenameLength) sb.Length = MaxFilenameLength;
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string? TryDecodeHexAscii(string s)
    {
        if (s.Length < 2 || s.Length % 2 != 0) return null;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return null;

        var bytes = new byte[s.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);

        // Only accept printable ASCII; control/NUL bytes mean it wasn't hex-encoded
        // text in the first place (e.g. a plain numeric serial that looks like hex).
        foreach (var b in bytes)
            if (b is < 0x20 or > 0x7E) return null;

        return Encoding.ASCII.GetString(bytes);
    }
}
