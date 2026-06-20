# Exclusive Mode

*Exclusive mode* is a full-device takeover. While a provider is active the host
suppresses all normal page mappings, freezes folder navigation, and routes
**every** hardware input on the active device to the provider. Use it for things
that own the whole device for a while: a telemetry HUD, a game overlay, a
screensaver, or streaming a video to the screen.

It differs from a [folder](Advanced-Folders): a folder is a navigable sub-view
that coexists with the normal UI; an exclusive provider takes over completely
until it is released.

A plugin enters exclusive mode through the host bridge:

```csharp
if (host.RequestExclusiveMode(myProvider))   // false if another provider already owns the device
{
    // we now own the device until ReleaseExclusiveMode(myProvider)
}
```

and leaves it with `host.ReleaseExclusiveMode(myProvider)`. `host.IsInExclusiveMode`
reports whether *any* provider is currently active. There is a single owner — the
host never steals an active provider's slot.

## IExclusiveModeProvider

```csharp
public interface IExclusiveModeProvider
{
    string Title { get; }

    void OnEnter();
    void OnExit();

    IReadOnlyList<FolderEntry> BuildTouchEntries();

    void OnSimpleButtonPressed(int index);
    void OnTouchPressed(int slotIndex);
    void OnRotaryPressed(int index);
    void OnRotated(int index, int delta);

    event EventHandler EntriesChanged;

    // Optional — default-implemented, so existing plugins need no changes:
    ExclusiveRenderMode RenderMode => ExclusiveRenderMode.FullScreen;
    int SingleTileSlot => 0;
}
```

| Member | Notes |
|---|---|
| `Title` | Shown by the host (status overlay). |
| `OnEnter()` | Called once when the host accepts the request. Subscribe to data sources here. |
| `OnExit()` | Called once when released (manually or at shutdown). **Unsubscribe everything from `OnEnter`.** |
| `BuildTouchEntries()` | Current touch-slot content (same [`FolderEntry`](Advanced-Folders#folderentry) type as folders). Slots you don't return are cleared to black. Keep it cheap and side-effect-free — the host calls it on every redraw. |
| `OnSimpleButtonPressed` / `OnTouchPressed` / `OnRotaryPressed` / `OnRotated` | Raw hardware input while you own the device. Indices are zero-based; `delta` is positive for clockwise/right. |
| `EntriesChanged` | Raise to tell the host the displayed data changed and the slots must be redrawn. |
| `RenderMode` | How the host pushes your frames to the device. See below. Defaults to `FullScreen`. |
| `SingleTileSlot` | Target slot for `SingleTile` mode only; ignored otherwise. Defaults to 0. |

The touch grid is the device's standard 5×3 layout (15 slots, see
[`FolderLayout`](Advanced-Folders#folderlayout)). Unlike folders, exclusive mode
has **no reserved back-button slot** — all 15 slots are yours.

## ExclusiveRenderMode

The device is bottlenecked by the serial transfer, not by drawing: a full
480×270 screen is ~259 KB on the wire, and a `DRAW` (display refresh) costs
almost nothing next to that. So the lever for higher frame rates is **sending
fewer / smaller framebuffers**. `RenderMode` picks the strategy:

```csharp
public enum ExclusiveRenderMode
{
    FullScreen,  // 0 — composite all slots, one full-screen blit + DRAW
    Grid,        // 1 — every slot as its own 90x90 tile, no DRAW
    DirtyTiles,  // 2 — only the tiles whose content changed, no DRAW
    SingleTile   // 3 — one slot (SingleTileSlot), no DRAW
}
```

| Mode | What the host sends per frame | Best for |
|---|---|---|
| `FullScreen` *(default)* | One composited 480×270 framebuffer + a `DRAW` refresh. Safe and simple. | Low-frequency, whole-screen content — a **screensaver**, a static splash. |
| `Grid` | All 15 slots as individual 90×90 framebuffers, no `DRAW`. | Full-grid content that changes a lot every frame. |
| `DirtyTiles` | Only the slots whose **visible content changed** since the last frame, no `DRAW`. | Per-tile **live data** where just a few slots move each frame — telemetry HUDs, dashboards. |
| `SingleTile` | One 90×90 slot (`SingleTileSlot`), no `DRAW`. | Streaming a GIF/video onto a single button. |

Rough throughput on a Loupedeck Live S (≈115 kbit serial): a single 90×90 tile
reaches several hundred fps, while a full-screen blit tops out around ~44 fps.
`DirtyTiles` gets you close to the single-tile number whenever only a handful of
slots actually change.

### How DirtyTiles decides what changed

The host compares each slot's **visible signature** between frames — the
`FolderEntry`'s `Text`, `BackColor`, `TextColor`, `TextSize`, `Bold`, and a hash
of `Image`. If the signature is identical, the tile is skipped (no serial write
at all). Two consequences for your `BuildTouchEntries()`:

- **Quantize values you don't want to redraw.** Formatting a speed as
  `"{x:F0}"` means the tile only re-sends when the integer changes, not on every
  sub-unit jitter.
- **Anything that should animate must actually change a visible field.** A blink,
  for example, has to toggle a color (not rely on wall-clock time alone), or the
  host will see an identical signature and skip the redraw.

The cache resets automatically when the provider changes or re-enters, so the
first frame always repaints every slot.

## Example: a minimal HUD

```csharp
public sealed class SpeedHud : IExclusiveModeProvider
{
    private readonly IPluginHost _host;
    private int _speed;

    public SpeedHud(IPluginHost host) => _host = host;

    public string Title => "Speed HUD";

    // Only a couple of tiles move each frame → DirtyTiles.
    public ExclusiveRenderMode RenderMode => ExclusiveRenderMode.DirtyTiles;

    public event EventHandler? EntriesChanged;

    public void Update(int speed)        // called by your data source
    {
        _speed = speed;
        EntriesChanged?.Invoke(this, EventArgs.Empty);   // ask the host to redraw
    }

    public IReadOnlyList<FolderEntry> BuildTouchEntries() => new[]
    {
        new FolderEntry { SlotIndex = 0, Text = "EXIT", BackColor = PluginColor.FromRgb(0x80,0x10,0x10), Bold = true },
        new FolderEntry { SlotIndex = 1, Text = $"{_speed:F0}\nkm/h", TextSize = 22, Bold = true },
    };

    public void OnEnter() { }
    public void OnExit()  { }
    public void OnSimpleButtonPressed(int index) { if (index == 0) _host.ReleaseExclusiveMode(this); }
    public void OnTouchPressed(int slotIndex)    { if (slotIndex == 0) _host.ReleaseExclusiveMode(this); }
    public void OnRotaryPressed(int index) { }
    public void OnRotated(int index, int delta) { }
}
```

## Notes

- **Single owner.** `RequestExclusiveMode` returns `false` if another provider is
  already active — handle that instead of assuming success.
- **Always provide an exit.** You own every input, so the user can only leave via
  something you wire up (a button/slot that calls `ReleaseExclusiveMode`) or a
  host-level toggle. Releasing restores the normal page automatically.
- **`RenderMode` / `SingleTileSlot` are read per frame**, so you can switch
  strategy at runtime if needed (e.g. `Grid` on entry, `DirtyTiles` once steady).
- **Pick the cheapest mode that looks right.** `DirtyTiles` for live dashboards,
  `FullScreen` or `Grid` for a screensaver, `SingleTile` for one-button media.
