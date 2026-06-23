namespace LoupixDeck.LoupedeckDevice;

public class DisplayInfo(byte[] id, int width, int height)
{
    public byte[] Id { get; } = id;
    public int Width { get; } = width;
    public int Height { get; } = height;

    public DisplayInfo(string id, int width, int height) : this(System.Text.Encoding.UTF8.GetBytes(id), width, height) { }
    public DisplayInfo(ReadOnlySpan<byte> id, int width, int height) : this(id.ToArray(), width, height) { }
}