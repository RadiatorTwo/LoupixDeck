# Folder Navigation

A *folder* is a temporary, plugin-supplied view that takes over the touch
screen — a 5×3 grid of slots whose content and behavior the plugin defines.
Typical uses: a list of OBS scene collections, a CoolerControl profile picker,
a CPU/GPU sensor dashboard.

A plugin opens a folder by calling `host.OpenFolder(provider)`.

## IFolderProvider

```csharp
public interface IFolderProvider
{
    string Title { get; }
    IReadOnlyList<FolderEntry> BuildEntries();
    IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides { get; }
    void OnEnter();
    void OnExit();
    event Action EntriesChanged;
}
```

| Member | Notes |
|---|---|
| `Title` | Folder title shown by the host (header bar, breadcrumb). |
| `BuildEntries()` | Returns the current slot content. Called whenever the host needs to redraw — keep it cheap and side-effect-free (read from cached state). |
| `RotaryOverrides` | Per-rotary-encoder behavior while the folder is open. Keyed by rotary index (0 = first encoder). Encoders not present in the map keep their default behavior. |
| `OnEnter()` | Called once when the folder is pushed onto the stack. Subscribe to data sources here. |
| `OnExit()` | Called once when the folder is popped. **Unsubscribe everything you subscribed in `OnEnter`** — otherwise you leak handlers across re-entries. |
| `EntriesChanged` | Raise to tell the host the entries (or their displayed data) changed and the folder view must be redrawn. |

## FolderProviderBase

```csharp
public abstract class FolderProviderBase : IFolderProvider
{
    public abstract string Title { get; }
    public abstract IReadOnlyList<FolderEntry> BuildEntries();
    public virtual IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => …empty…;
    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public event Action? EntriesChanged;
    protected void RaiseEntriesChanged() => EntriesChanged?.Invoke();
}
```

Convenience base class. Inherit it instead of implementing the interface
directly — you get the `EntriesChanged` plumbing and an empty default for
`RotaryOverrides`. Call `RaiseEntriesChanged()` from your data-change handlers.

## FolderEntry

```csharp
public sealed class FolderEntry
{
    public int          SlotIndex  { get; init; }
    public string       Text       { get; init; } = string.Empty;
    public byte[]?      Image      { get; init; }
    public PluginColor  BackColor  { get; init; } = PluginColor.Black;
    public PluginColor  TextColor  { get; init; } = PluginColor.White;
    public int          TextSize   { get; init; } = 16;
    public bool         Bold       { get; init; }
    public Func<Task>?  OnPress    { get; init; }
    public IFolderProvider? OpensFolder { get; init; }
}
```

A single grid slot. Each entry either:

- runs `OnPress` when tapped (a leaf), or
- opens `OpensFolder` as a nested folder (the host pushes it onto the stack
  and renders a back button automatically).

`Image` is optional PNG-encoded bytes; when `null` the slot is text-only.

## FolderLayout

```csharp
public static class FolderLayout
{
    public const int BackSlotIndex = 10;  // 5x3 grid: row 2, col 0
    public const int TotalSlots    = 15;
    public const int Columns       = 5;
}
```

The grid is 5 columns × 3 rows = 15 slots. Slot 10 is reserved for the
host-drawn back button — do not put a `FolderEntry` there; it will be
overdrawn. Slots 0–9 and 11–14 are yours.

## RotaryOverride

```csharp
public sealed class RotaryOverride
{
    public Func<Task>? OnLeft  { get; init; }
    public Func<Task>? OnRight { get; init; }
    public Func<Task>? OnPress { get; init; }
}
```

Use when the folder needs the rotary encoders to do something context-specific
— e.g. scroll a long list, dial a value, confirm a selection.

## PluginColor

```csharp
public readonly record struct PluginColor(byte R, byte G, byte B, byte A = 255)
{
    public static PluginColor Black { get; }
    public static PluginColor White { get; }
    public static PluginColor FromRgb(byte r, byte g, byte b);
}
```

Plain RGBA. The SDK is UI-framework-agnostic, so it does not expose Avalonia
or SkiaSharp color types — the host converts as needed.

## Example: scene picker

```csharp
internal sealed class ScenePickerFolder(Func<IPluginHost> hostAccessor) : FolderProviderBase
{
    private List<string> _scenes = [];

    public override string Title => "Scenes";

    public override void OnEnter()
    {
        ObsClient.ScenesChanged += OnScenesChanged;
        _ = RefreshAsync();
    }

    public override void OnExit() => ObsClient.ScenesChanged -= OnScenesChanged;

    private void OnScenesChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        _scenes = await ObsClient.GetScenesAsync();
        RaiseEntriesChanged();
    }

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        var entries = new List<FolderEntry>();
        for (int i = 0; i < _scenes.Count && i < FolderLayout.TotalSlots; i++)
        {
            if (i == FolderLayout.BackSlotIndex) continue;
            var scene = _scenes[i];
            entries.Add(new FolderEntry
            {
                SlotIndex = i,
                Text      = scene,
                BackColor = PluginColor.FromRgb(40, 40, 80),
                OnPress   = () => ObsClient.SwitchSceneAsync(scene)
            });
        }
        return entries;
    }
}
```
