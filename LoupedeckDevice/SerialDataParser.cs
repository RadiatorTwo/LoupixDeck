
namespace LoupixDeck.LoupedeckDevice;

public class SerialDataParser
{
    // Internal buffer to which all received bytes are appended.
    private readonly List<byte> _buffer = new();

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

        // Append new data to the buffer.
        _buffer.AddRange(data.Take(bytesRead));
        Trace($"Added {bytesRead} new bytes. Buffer length: {_buffer.Count}");

        // As long as the buffer contains data that may hold a complete command...
        while (true)
        {
            // If the buffer is empty, stop processing.
            if (_buffer.Count == 0)
            {
                Trace("Buffer is empty.");
                break;
            }

            // Check if the first byte matches the expected start byte (130).
            if (_buffer[0] != 130)
            {
                // Discard all bytes until the next start byte is found.
                int index = _buffer.IndexOf(130);
                if (index == -1)
                {
                    Trace($"No start byte (130) found in the buffer. Discarding all {_buffer.Count} bytes.");
                    _buffer.Clear();
                    break;
                }
                else
                {
                    Trace($"Invalid bytes found at the beginning. Removing {index} bytes until the next start byte.");
                    _buffer.RemoveRange(0, index);
                }
            }

            // At this point, the first byte is guaranteed to be 130.
            // At least 2 bytes are required to determine the length of the command.
            if (_buffer.Count < 2)
            {
                Trace("Not enough bytes to determine the length. Waiting for more data.");
                break;
            }

            // The second byte specifies the length of the command (number of bytes after the first two header bytes).
            int commandLength = _buffer[1];
            // Total command length: Start byte (1) + Length byte (1) + commandLength
            int totalCommandLength = 2 + commandLength;

            // Check if the buffer contains a complete command.
            if (_buffer.Count < totalCommandLength)
            {
                Trace($"Incomplete command: Expected {totalCommandLength} bytes, but buffer contains only {_buffer.Count}. Waiting for more data.");
                break;
            }

            // A complete command is available.
            // Extract the command without the start byte (130) and the length byte.
            // The 'command' array contains all bytes starting after the length byte.
            byte[] command = _buffer.Skip(2).Take(totalCommandLength - 2).ToArray();
            Trace($"Complete command found. Length: {command.Length} bytes. Command: {BitConverter.ToString(command)}");

            // Trigger event
            PacketReceived?.Invoke(command);
            Trace("PacketReceived event triggered.");

            // Remove the processed command from the buffer.
            _buffer.RemoveRange(0, totalCommandLength);
            Trace($"Removed {totalCommandLength} bytes from the buffer. New buffer length: {_buffer.Count}");
        }
    }
}
