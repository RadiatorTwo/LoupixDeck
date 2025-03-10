namespace LoupixDeck.LoupedeckDevice;

public class MagicByteLengthParser(byte magicByte)
{
    private readonly List<byte> _buffer = [];
    private int _bytesRemainingInPacket;
    private bool _inPacket;

    public List<byte[]> ProcessData(byte[] data, int count)
    {
        var completedPackets = new List<byte[]>();
        var offset = 0;

        while (offset < count)
        {
            if (!_inPacket)
            {
                // Searching for the magic byte
                if (data[offset] == magicByte)
                {
                    // Determine the length
                    if (offset + 1 < count)
                    {
                        byte lengthInfo = data[offset + 1];
                        if (lengthInfo == 0xFF)
                        {
                            // "Large packet"
                            // We assume that the 4 bytes come from index 6..9
                            // Proper extraction would require more state handling
                            // Possibly process in multiple steps.
                            // Simplified here: we attempt to read directly
                            if (offset + 10 <= count) // 0x82 + 0xFF + (at least 8 bytes?)
                            {
                                // Read 4 bytes Big Endian from offset+6
                                _bytesRemainingInPacket = ReadUInt32Be(data, offset + 6);
                                _inPacket = true;
                                _buffer.Clear();
                                offset += 10; // Consume header
                            }
                            else
                            {
                                // Not enough data yet, wait
                                break;
                            }
                        }
                        else
                        {
                            // "Small packet"
                            var smallLen = (lengthInfo & 0x7F);
                            _bytesRemainingInPacket = smallLen;
                            _inPacket = true;
                            _buffer.Clear();
                            offset += 2; // Magic byte + length byte
                        }
                    }
                    else
                    {
                        // We have only the magic byte but no length byte yet
                        break;
                    }
                }
                else
                {
                    // Byte does not match -> ignore
                    offset++;
                }
            }
            else
            {
                // We are in the middle of a packet
                var bytesToTake = Math.Min(_bytesRemainingInPacket, count - offset);
                _buffer.AddRange(new ArraySegment<byte>(data, offset, bytesToTake));

                _bytesRemainingInPacket -= bytesToTake;
                offset += bytesToTake;

                if (_bytesRemainingInPacket != 0) continue;
                
                // Packet complete
                completedPackets.Add(_buffer.ToArray());
                _inPacket = false;
                _buffer.Clear();
            }
        }

        return completedPackets;
    }

    private static int ReadUInt32Be(byte[] data, int startIndex)
    {
        return (data[startIndex] << 24)
               | (data[startIndex + 1] << 16)
               | (data[startIndex + 2] << 8)
               | (data[startIndex + 3]);
    }
}
