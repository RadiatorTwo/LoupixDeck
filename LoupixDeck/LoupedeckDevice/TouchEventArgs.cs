namespace LoupixDeck.LoupedeckDevice;

public class TouchEventArgs : EventArgs
{
    public required Constants.TouchEventType EventType { get; set; } // "touchstart", "touchmove", "touchend"
    public required List<TouchInfo> Touches { get; set; }
    public required TouchInfo ChangedTouch { get; set; }
}