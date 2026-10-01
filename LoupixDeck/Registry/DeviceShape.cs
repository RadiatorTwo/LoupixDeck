namespace LoupixDeck.Registry;

/// <summary>The control layout of the attached device that a profile is built for.</summary>
/// <param name="TouchButtonCount">Touch slots per page, including any side-strip slots.</param>
/// <param name="RotaryButtonCount">Dials on a shared (both-sides) rotary page.</param>
/// <param name="SideRotaryButtonCount">Dials on one side's rotary page.</param>
/// <param name="HasIndependentRotarySides">True when the device has left and right dial columns
/// with their own pages and side strips (Razer Stream Controller).</param>
/// <param name="Geometry">Pixel geometry, used to place icons and labels.</param>
public sealed record DeviceShape(
    int TouchButtonCount,
    int RotaryButtonCount,
    int SideRotaryButtonCount,
    bool HasIndependentRotarySides,
    DeviceGeometry Geometry);