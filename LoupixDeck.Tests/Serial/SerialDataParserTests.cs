using System.Collections.Generic;
using LoupixDeck.LoupedeckDevice;
using Xunit;

namespace LoupixDeck.Tests.Serial;

public class SerialDataParserTests
{
    [Fact]
    public void CompletePacketTriggersEvent()
    {
        // Arrange
        var parser = new SerialDataParser();
        List<byte[]> received = new();
        parser.PacketReceived += p => received.Add(p);
        byte[] packet = {130, 3, 1, 2, 3};

        // Act
        parser.ProcessReceivedData(packet, packet.Length);

        // Assert
        Assert.Single(received);
        Assert.Equal(new byte[]{1,2,3}, received[0]);
    }

    [Fact]
    public void IncompletePacketBufferedUntilCompletion()
    {
        // Arrange
        var parser = new SerialDataParser();
        List<byte[]> received = new();
        parser.PacketReceived += p => received.Add(p);
        byte[] part1 = {130, 3, 9};
        byte[] part2 = {8, 7};

        // Act
        parser.ProcessReceivedData(part1, part1.Length);
        // No full packet yet
        Assert.Empty(received);
        parser.ProcessReceivedData(part2, part2.Length);

        // Assert
        Assert.Single(received);
        Assert.Equal(new byte[]{9,8,7}, received[0]);
    }

    [Fact]
    public void MalformedDataDiscardedUntilStartByte()
    {
        // Arrange
        var parser = new SerialDataParser();
        List<byte[]> received = new();
        parser.PacketReceived += p => received.Add(p);
        byte[] data = {1, 2, 130, 2, 5, 6};

        // Act
        parser.ProcessReceivedData(data, data.Length);

        // Assert
        Assert.Single(received);
        Assert.Equal(new byte[]{5,6}, received[0]);
    }
}
