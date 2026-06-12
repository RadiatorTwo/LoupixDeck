namespace LoupixDeck.Models;

/// <summary>
/// Identifies which dial column a rotary page / paging action belongs to.
///
/// <see cref="Both"/> is the legacy single-column model still used by devices
/// without side strips (e.g. the Loupedeck Live S): one page holds every knob and
/// both sides page together. Devices with side strips (Razer Stream Controller)
/// keep an independent set of pages per side, tagged <see cref="Left"/> / <see cref="Right"/>,
/// so swiping one strip only cycles that side.
/// </summary>
public enum RotarySide
{
    Both = 0,
    Left = 1,
    Right = 2
}
