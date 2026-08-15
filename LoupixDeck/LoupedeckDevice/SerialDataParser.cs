using LoupixDeck.LoupedeckDevice.Device;

namespace LoupixDeck.LoupedeckDevice;

public class SerialDataParser
{
    // A well-formed frame is at most 2 header bytes + 255 payload bytes = 257 bytes.
    // If the buffer ever grows far beyond that without yielding a complete frame, the
    // stream is desynced or we are receiving garbage. Cap it so a permanent bad-frame
    // condition cannot grow the buffer without bound (OOM). 4 KiB leaves ample headroom
    // for a legitimate partial frame while still bounding runaway growth.
    private const int MaxBufferSize = 4096;
    private const byte StartByte = 130;

    private readonly byte[] _buffer = new byte[MaxBufferSize];
    private int _offset;
    private int _length;

    // Wire-protocol tracing emits several lines per received byte — useful when
    // debugging the framing, but it floods the console during normal operation
    // (and drowns out everything else). Off by default; flip to true here when
    // you need to inspect the raw protocol.
    private static readonly bool TraceEnabled = false;

    private static void Trace(string message)
    {
        if (TraceEnabled) Console.WriteLine(message);
    }

    // Event triggered when a complete command (excluding the start byte) is received.
    public event Action<byte[]> PacketReceived;

    /// <summary>
    /// Processes newly received data from the serial device.
    /// </summary>
    /// <param name="data">The byte array containing the received data.</param>
    /// <param name="bytesRead">The number of bytes actually read.</param>
    public void ProcessReceivedData(byte[] data, int bytesRead)
    {
        if (data == null || bytesRead <= 0)
        {
            Trace("No data to process.");
            return;
        }

        ReadOnlySpan<byte> incoming = data.AsSpan(0, Math.Min(bytesRead, data.Length));
        if (!TryAppend(incoming))
            return;

        Trace($"Added {incoming.Length} new bytes. Buffer length: {_length}");

        while (_length > 0)
        {
            Span<byte> live = _buffer.AsSpan(_offset, _length);

            if (live[0] != StartByte)
            {
                int index = live.IndexOf(StartByte);
                if (index < 0)
                {
                    Trace($"No start byte ({StartByte}) found in the buffer. Discarding all {_length} bytes.");
                    if (SendDiagnostics.Enabled)
                        SendDiagnostics.OnFraming($"no start byte in {_length} buffered bytes; discarding all (stream desync)");
                    Clear();
                    break;
                }

                Trace($"Invalid bytes found at the beginning. Removing {index} bytes until the next start byte.");
                // A resync means the previous frame boundary was wrong, so a response
                // frame was likely lost here — the most probable cause of a #149 timeout.
                if (SendDiagnostics.Enabled)
                    SendDiagnostics.OnFraming($"resync: discarded {index} byte(s) before next start byte (likely lost a response frame)");
                Discard(index);
                continue;
            }

            // At this point, the first byte is guaranteed to be 130.
            // At least 2 bytes are required to determine the length of the command.
            if (_length < 2)
            {
                Trace("Not enough bytes to determine the length. Waiting for more data.");
                break;
            }

            // The second byte specifies the length of the command (number of bytes after the first two header bytes).
            // NOTE: only WebSocket payload lengths <=125 are handled; 126/127 mean a
            // 16-/64-bit extended length follows and would be mis-decoded. Flag it (debug
            // only) so we know if a real frame ever takes this path (a candidate #149 cause).
            byte lengthByte = _buffer[_offset + 1];
            if (SendDiagnostics.Enabled && lengthByte is 126 or 127)
                SendDiagnostics.OnFraming($"length byte 0x{lengthByte:x2} indicates a WebSocket extended length that is not decoded (frame will desync)");

            int commandLength = lengthByte;
            int totalCommandLength = 2 + commandLength;

            if (_length < totalCommandLength)
            {
                Trace($"Incomplete command: Expected {totalCommandLength} bytes, but buffer contains only {_length}. Waiting for more data.");
                break;
            }

            // Payload after the start and length bytes. Copied because the handler
            // retains the array and the sliding window will overwrite this region.
            byte[] command = _buffer.AsSpan(_offset + 2, commandLength).ToArray();
            Trace($"Complete command found. Length: {command.Length} bytes. Command: {BitConverter.ToString(command)}");

            PacketReceived?.Invoke(command);
            Trace("PacketReceived event triggered.");

            Discard(totalCommandLength);
            Trace($"Removed {totalCommandLength} bytes from the buffer. New buffer length: {_length}");
        }
    }

    /// <summary>
    /// Appends <paramref name="incoming"/> to the sliding window. Compacts toward
    /// index 0 when the tail runs out of room. Returns false and drops the window
    /// (including the new bytes) if the result would exceed <see cref="MaxBufferSize"/>.
    /// </summary>
    private bool TryAppend(ReadOnlySpan<byte> incoming)
    {
        if (_length + incoming.Length > MaxBufferSize)
        {
            Trace($"Buffer exceeded {MaxBufferSize} bytes ({_length + incoming.Length}); discarding to prevent unbounded growth.");
            if (SendDiagnostics.Enabled)
                SendDiagnostics.OnFraming($"buffer exceeded {MaxBufferSize} bytes; discarding {_length + incoming.Length} bytes (hard desync)");
            Clear();
            return false;
        }

        if (_offset + _length + incoming.Length > _buffer.Length)
        {
            _buffer.AsSpan(_offset, _length).CopyTo(_buffer);
            _offset = 0;
        }

        incoming.CopyTo(_buffer.AsSpan(_offset + _length));
        _length += incoming.Length;
        return true;
    }

    private void Discard(int count)
    {
        _offset += count;
        _length -= count;
        if (_length == 0)
            _offset = 0;
    }

    private void Clear()
    {
        _offset = 0;
        _length = 0;
    }
}
