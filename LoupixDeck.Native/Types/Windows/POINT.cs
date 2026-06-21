namespace LoupixDeck.Native.Types.Windows;

/// <summary>
/// The POINT structure defines the x- and y-coordinates of a point.
/// </summary>
/// <remarks>
/// see <see href="https://learn.microsoft.com/en-us/windows/win32/api/windef/ns-windef-point">microslop's docs</see> for more details
/// </remarks>
public struct POINT
{
    public int X;
    public int Y;
}
