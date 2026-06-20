namespace LoupixDeck.PluginSdk;

/// <summary>
/// Touch-button grid geometry for the folder navigation view. A folder entry's
/// <see cref="FolderEntry.SlotIndex"/> addresses one cell of this grid; the
/// back-button slot is reserved by the host.
/// </summary>
public static class FolderLayout
{
    /// <summary>Reserved slot for the host-drawn back button (5x3 grid: row 2, col 0).</summary>
    public const int BackSlotIndex = 10;

    /// <summary>Total addressable slots.</summary>
    public const int TotalSlots = 15;

    /// <summary>Columns in the grid.</summary>
    public const int Columns = 5;
}
