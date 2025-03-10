namespace LoupixDeck.LoupedeckDevice;

public class ButtonEventArgs : EventArgs
{
    public string ButtonId { get; set; }
    public string EventType { get; set; }
}