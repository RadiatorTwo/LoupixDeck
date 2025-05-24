namespace LoupixDeck.LoupedeckDevice;

public class TouchInfo
{
    public int X { get; set; }
    public int Y { get; set; }
    public byte Id { get; set; }
    public required TouchTarget Target { get; set; }
}