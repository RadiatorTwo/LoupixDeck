namespace LoupixDeck.PluginSdk;

/// <summary>
/// Plugin-supplied controller for the global exclusive mode. While a provider
/// is active, the host suppresses normal page-mappings, freezes the folder
/// navigation, and routes every hardware input on the active device to this
/// provider. Use it for full-device takeovers like a telemetry HUD or a
/// game-specific overlay; for sub-navigation prefer <see cref="IFolderProvider"/>.
/// </summary>
public interface IExclusiveModeProvider
{
    /// <summary>Title shown by the host (e.g. on a status overlay).</summary>
    string Title { get; }

    /// <summary>Called once when the host has accepted the exclusive request.</summary>
    void OnEnter();

    /// <summary>Called once when the provider is released (manually or by shutdown).</summary>
    void OnExit();

    /// <summary>Current touch-slot content (up to the active device's slot count).
    /// Slots not returned are cleared to black by the host.</summary>
    IReadOnlyList<FolderEntry> BuildTouchEntries();

    /// <summary>A simple (hardware) button was pressed. Index is zero-based.</summary>
    void OnSimpleButtonPressed(int index);

    /// <summary>A touch slot was tapped. Index is the slot index.</summary>
    void OnTouchPressed(int slotIndex);

    /// <summary>A rotary encoder was pressed.</summary>
    void OnRotaryPressed(int index);

    /// <summary>A rotary encoder turned. <paramref name="delta"/> is positive for
    /// clockwise / right and negative for counter-clockwise / left.</summary>
    void OnRotated(int index, int delta);

    /// <summary>Raised when the touch entries (or their displayed data) changed
    /// and the host must redraw the slots.</summary>
    event EventHandler EntriesChanged;

    /// <summary>
    /// How the host should push this provider's frames to the device. Defaults to
    /// <see cref="ExclusiveRenderMode.FullScreen"/> (one composited blit + DRAW).
    /// Override to opt into per-tile strategies — e.g. <see cref="ExclusiveRenderMode.DirtyTiles"/>
    /// for a telemetry HUD that only changes a few slots per frame.
    /// </summary>
    ExclusiveRenderMode RenderMode => ExclusiveRenderMode.FullScreen;

    /// <summary>
    /// Target slot index for <see cref="ExclusiveRenderMode.SingleTile"/>. Ignored
    /// by every other mode. Defaults to slot 0.
    /// </summary>
    int SingleTileSlot => 0;
}
