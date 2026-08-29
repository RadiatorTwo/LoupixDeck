namespace LoupixDeck.Registry;

/// <summary>
/// Pixel geometry and hardware capabilities of one device model — the single source of
/// truth for every size the renderer, the layer editor and the framebuffer path need.
///
/// Before this record the touch-key tile was a literal 90 repeated across ~30 call sites
/// and the panel a literal 480x270 duplicated in three places. Both are per-device: the
/// Razer Stream Controller X draws 96px keys onto a 480x288 panel. Sizes must therefore
/// come from here rather than from a constant, so the framebuffer is pixel-exact and no
/// scaling step is needed anywhere.
/// </summary>
public sealed record DeviceGeometry
{
    /// <summary>Edge length of one centre-grid touch key, in device pixels. Keys are square.</summary>
    public required int KeySize { get; init; }

    /// <summary>
    /// Width of the unified panel the touch coordinates and the wallpaper live on.
    /// This is the whole surface including any side strips — on the Loupedeck CT it stays
    /// 480 even though the device draws it through four separate framebuffers
    /// (60 left + 360 centre + 60 right).
    /// </summary>
    public required int PanelWidth { get; init; }

    /// <summary>Height of the unified panel, in device pixels.</summary>
    public required int PanelHeight { get; init; }

    /// <summary>
    /// Width of one side display strip, or 0 when the device has none. The strips occupy
    /// the outer columns of the panel and are <see cref="PanelHeight"/> tall.
    /// </summary>
    public int StripWidth { get; init; }

    /// <summary>
    /// True when the centre grid is a set of physical keys rather than a touchscreen.
    /// Such a device reports key presses as BUTTON_PRESS frames; the device class
    /// translates them into synthetic touches so the whole consumer path is unchanged.
    /// </summary>
    public bool PhysicalKeys { get; init; }

    /// <summary>False on devices without a haptic motor — SET_VIBRATION is then not sent.</summary>
    public bool HasVibration { get; init; } = true;

    /// <summary>False on devices without addressable LED buttons — SET_COLOR is then not sent.</summary>
    public bool HasLedButtons { get; init; } = true;

    /// <summary>Size of one full-panel BGRA frame, as used by the full-display animation path.</summary>
    public int FrameBytes => PanelWidth * PanelHeight * 4;

    /// <summary>
    /// Geometry of the Loupedeck family as it was hard-coded before this record existed.
    /// Used as the fallback wherever no device is resolved yet, so those paths keep
    /// behaving exactly as they did.
    /// </summary>
    public static readonly DeviceGeometry Default = new()
    {
        KeySize = 90,
        PanelWidth = 480,
        PanelHeight = 270,
        StripWidth = 60
    };
}
