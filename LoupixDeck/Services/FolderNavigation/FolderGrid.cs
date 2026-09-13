using LoupixDeck.Registry;

namespace LoupixDeck.Services.FolderNavigation;

/// <summary>
/// Folder-mode geometry of one device model. Replaces <see cref="FolderConstants"/>, whose
/// fixed 5x3 silently mis-addressed the three 4x3 models: their slots 12 and 13 are the side
/// strips and slot 14 does not exist at all, so the repaint loop painted into the strips and
/// then threw.
/// </summary>
public sealed record FolderGrid
{
    public required int Columns { get; init; }
    public required int Rows { get; init; }

    /// <summary>Addressable keys in the centre grid — the only slots a folder may paint.</summary>
    public int GridSlots => Columns * Rows;

    /// <summary>Bottom-left key, reserved by the host for the back button.</summary>
    public int BackSlotIndex => Columns * (Rows - 1);

    public bool HasSideStrips { get; init; }

    /// <summary>
    /// Slot index of the left strip, or -1 on a device without strips. Slots
    /// <see cref="GridSlots"/> and <see cref="GridSlots"/> + 1 because that is exactly what
    /// the two strip-carrying device classes declare: <c>RazerStreamControllerDevice.LeftSideIndex</c>
    /// is 12 and <c>RightSideIndex</c> is 13 on a 4x3 grid.
    /// </summary>
    public int LeftStripSlot => HasSideStrips ? GridSlots : -1;

    /// <summary>Slot index of the right strip, or -1 on a device without strips.</summary>
    public int RightStripSlot => HasSideStrips ? GridSlots + 1 : -1;

    /// <summary>True when the slot addresses a real key of the centre grid.</summary>
    public bool IsGridSlot(int slot) => slot >= 0 && slot < GridSlots;

    /// <summary>The 5x3 fallback used when no device geometry is available yet.</summary>
    public static readonly FolderGrid Default = new() { Columns = 5, Rows = 3, HasSideStrips = false };

    public static FolderGrid From(DeviceGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return new FolderGrid
        {
            Columns = geometry.Columns,
            Rows = geometry.Rows,
            HasSideStrips = geometry.StripWidth > 0
        };
    }

    /// <summary>
    /// Verifies that the registry geometry and the live device class agree about the grid.
    /// They are two declarations of the same fact (see DeviceGeometry.Columns) and a silent
    /// disagreement would put the back button on the wrong key.
    /// </summary>
    public bool Matches(int deviceColumns, int deviceRows) =>
        Columns == deviceColumns && Rows == deviceRows;
}
