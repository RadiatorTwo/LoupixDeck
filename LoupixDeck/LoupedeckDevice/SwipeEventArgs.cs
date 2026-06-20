namespace LoupixDeck.LoupedeckDevice;

/// <summary>Which side strip a swipe occurred on.</summary>
public enum SideStrip
{
    Left,
    Right
}

/// <summary>Vertical swipe direction on a side strip.</summary>
public enum SwipeDirection
{
    Up,
    Down
}

/// <summary>
/// Raised by <see cref="Device.LoupedeckDevice.OnSwipe"/> when a vertical swipe is
/// detected on one of the side strips. The controller maps this to paging the
/// matching dial column.
/// </summary>
public class SwipeEventArgs : EventArgs
{
    public required SideStrip Side { get; init; }
    public required SwipeDirection Direction { get; init; }
}
