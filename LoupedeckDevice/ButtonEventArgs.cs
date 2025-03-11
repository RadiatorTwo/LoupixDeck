namespace LoupixDeck.LoupedeckDevice;

public class ButtonEventArgs : EventArgs
{
    public string ButtonId { get; set; }
    public Constants.ButtonEventType EventType { get; set; }
}