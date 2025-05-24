namespace LoupixDeck.LoupedeckDevice;

public class ButtonEventArgs : EventArgs
{
    public Constants.ButtonType ButtonId { get; set; }
    public Constants.ButtonEventType EventType { get; set; }
}