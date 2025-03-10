namespace LoupixDeck.LoupedeckDevice;

public class RotateEventArgs : EventArgs
{
    public string ButtonId { get; set; }
    public sbyte Delta { get; set; }
}