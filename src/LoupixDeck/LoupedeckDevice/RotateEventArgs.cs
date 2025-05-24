namespace LoupixDeck.LoupedeckDevice;

public class RotateEventArgs : EventArgs
{
    public Constants.ButtonType ButtonId { get; set; }
    public sbyte Delta { get; set; }
}