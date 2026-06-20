namespace LoupixDeck.LoupedeckDevice;

public class MessageEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; set; } = data;
}