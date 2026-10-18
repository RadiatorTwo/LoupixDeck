# Recent SDK additions

LoupixDeck v1.47.0 provides SDK 1.33.0. The following contracts supplement the
existing API pages. The SDK targets `net9.0` and `net10.0` and is published on
nuget.org. All additions preserve existing plugins, but a plugin using a newer
member needs a host that supplies it. Declare the SDK version you build against
in `plugin.json`; the Plugin Store checks that version before offering a release.

| SDK | First host release | Additions |
| --- | --- | --- |
| 1.24.0 | v1.32.0 | Plugin translation files, `IPluginHost.Tr` and `CurrentLanguage`; published NuGet package |
| 1.25.0 | v1.33.0 | `AdjustmentValue`, `SideStripRotary.GetValue`, `CommandMigration`, `LoupixPlugin.GetCommandMigrations()`; host dispatch of adjustment commands |
| 1.26.0 | v1.34.0 | `CommandContext.ButtonKey`, `IRenderCanvas.DrawPixels` |
| 1.27.0 | v1.37.0 | `CommandDescriptor.ButtonLayout` and its layer descriptors |
| 1.28.0 | v1.37.0 | `FolderEntry.Render`, `IPluginRequirements`, `PluginRequirement` |
| 1.29.0 | v1.41.0 | `IValueDisplayCommand`, `AdjustmentValue.Detail`; indicator layers, value text sources and a background colour in button layouts |
| 1.30.0 | v1.41.0 | `ButtonStateDescriptor.Layout`: starting layers per declared state |
| 1.31.0 | v1.42.0 | `IFolderSideStripProvider`, `RotaryOverride.Label` and `GetValue` |
| 1.32.0 | v1.43.0 | Wheel render/input APIs, separate wheel exclusive scope, and the `loupix` plugin tool |
| 1.33.0 | v1.44.0 | Architecture-aware plugin manifests, release packages, and tool validation; no runtime-contract change |

## Plugin translations

Ship `strings.de.json`, `strings.es.json`, or other supported language files
beside `plugin.json`. Keys are the original English text, not host localization
keys. For example:

```json
{
  "My Plugin": "Mein Plugin",
  "Volume": "Lautstärke"
}
```

The host translates descriptor text: plugin names/descriptions, command names,
groups/descriptions, parameter names, and setting labels. Each plugin has its
own namespace. Lookup uses the active-language file, then `strings.en.json`,
then the host catalogue, and finally the original text. Missing files are valid.
Keep internal command ids unchanged.

For text built at runtime, call `host.Tr("Volume")`; read
`host.CurrentLanguage` when needed rather than caching it at initialization.
Switching language updates host UI immediately, while text already drawn by a
plugin changes when that plugin redraws. Include the translation files in the
release package.

## Dial adjustment commands and migrations

Implement `IAdjustmentCommand` for a value controlled by a dial. A turn calls
`ApplyAdjustment(CommandContext ctx, int ticks)` with positive ticks for right
and negative ticks for left. A press calls `ApplyReset(CommandContext ctx)`.
Assigning one turn slot is enough: empty opposite-turn and press slots borrow
that adjustment command. Explicit bindings take precedence. Non-dial targets
still call `Execute`, so implement an appropriate fallback.

`GetValue(CommandContext ctx)` returns `AdjustmentValue?`. Its `Normalized`
position is 0–1 and is clamped by the host; its optional `Text` might be `"75%"`.
Return `null` for no bar. `GetValueText` defaults to that value's text and can be
overridden for text-only output. Value reads run during rendering and must be
fast, synchronous, and free of side effects. Call
`host.RequestButtonRefresh(Descriptor.CommandName)` when backend state changes.
Custom side-strip sessions can read the same live value through the
`SideStripRotary.GetValue` delegate instead of parsing bindings themselves.

Override `LoupixPlugin.GetCommandMigrations()` to return `CommandMigration`
rules when replacing old per-gesture commands. Each rule has a stable `Id`, a
`From` map of `RotaryAction` to old command name, a `To` command name, and an
optional parameter map. A value such as `"{oldName}"` carries an old parameter
forward; an omitted value uses the new command's default. The host only rewrites
matching single-command bindings with consistent parameters. It records
`pluginId:Id` once, backs up the config before saving, and does not rerun a rule
after the user edits the dial. Never reuse an id for a different migration.

## Per-button state and pixel rendering

`CommandContext.ButtonKey` is an opaque identity shared by a touch button's
render and execute calls. Two otherwise identical buttons have different keys,
so a paging tile can keep its own page selection. It is a runtime handle:
never save it, and allow for `null` when no button is involved or on older hosts.

`IRenderCanvas.DrawPixels(ReadOnlySpan<uint> pixels, int width, int height,
int x = 0, int y = 0)` composites a row-major frame 1:1. Each pixel is
`0xAARRGGBB` with straight alpha, not premultiplied alpha. There is no scaling
or decoding; the input must contain at least `width * height` values.

## Initial button layouts

Set `CommandDescriptor.ButtonLayout` to a `ButtonLayoutDescriptor` to describe
the ordinary layers created on touch assignment. Modes are `Default`, `None`,
`IconOnly`, `CaptionOnly`, `IconAndCaption`, and `Custom`. Use `None` when the
command paints the complete key and an extra caption would obscure it.
An omitted layout keeps the host's standard behavior.

For `Custom`, provide `ButtonLayerDescriptor` items in bottom-to-top order.
`Kind` is `Symbol`, `Text`, `Image`, or (since SDK 1.29.0) `Indicator`; symbols can use a full-catalog MDI
`Glyph` or SVG/bitmap `ImageData`. Image data is stored in the host asset store
at assignment. `KeepOriginalColors` can override automatic icon tinting.
Offsets and text sizes use a 90-pixel reference key and scale to the device;
`IconScale` preserves the picture's aspect ratio. Keep embedded data small.

The actions panel applies this layout when assigning the command. The button
editor applies a declared layout when the first command is added to a button
with an empty layer list. These are normal,
editable layers created once; runtime display commands still render over them.

## Touch-key values drawn by the key's own layers

Implement `IValueDisplayCommand` on a touch-button command to report a value
instead of drawing the key. `GetValue` returns an `AdjustmentValue` (the same
type adjustment dials use) or `null`; the host polls it on `UpdateInterval`, and
`host.RequestButtonRefresh` re-reads it at once. The key's layers draw it:

- an **Indicator** layer draws an arc up to `Normalized` (0–1); `double.NaN`
  draws no arc;
- a text layer whose *Text source* is **Value** shows `Text`, and one set to
  **Value detail** shows the new `AdjustmentValue.Detail`.

Declare the starting layers in the layout: `ButtonLayerKind.Indicator` (with
`Color` as the fill, `TrackColor`, `Thickness`, `StartAngle`, `SweepAngle`, and
`IconScale` for its size) and `ButtonLayerDescriptor.TextSource`.
`ButtonLayoutDescriptor.BackgroundColor` sets the key's own background colour.
The user can add, move and restyle these layers in the button editor like any
other, which is the point: a gauge stays fully stylable without the plugin
drawing anything. The interface combines with `IDisplayCommand` and
`IDisplayImageCommand`.

Since SDK 1.30.0, a command that declares button states can give each state its
own starting look with `ButtonStateDescriptor.Layout`, for example an hourglass
while busy and a bell while waiting. When the host creates the command's states
on a touch key, it fills each state that has no layers yet from that layout. It
never replaces existing layers. Set the command's `ButtonLayout` to
`ButtonLayoutMode.None` when every state brings its own.

## Pixel-exact folder slots

Set `FolderEntry.Render` to an `Action<IRenderCanvas>` for drawings that must
match the key's actual size. Read the canvas `Width` and `Height`, including
the current device calibration, rather than assuming 90 pixels. The host
composes background colour, `Image`, `Render`, then `Text`, in that order.
Use `DrawPixels` for an unscaled framebuffer if needed.

The callback runs under the host render lock when the folder repaints. Draw
from already captured state, return quickly, never block or touch UI, and do
not keep the canvas after the call. An exception is logged and skips that layer.
Continue using `host.FolderGrid` for slot placement and its reserved Back slot.

## System requirements

A plugin can implement `IPluginRequirements.GetRequirements()` returning an
`IReadOnlyList<PluginRequirement>`. Each entry has required `Id`, `Name`, and
`IsMet` fields, plus optional `Message` and `InstallHint`. Keep ids stable and
author text in English; the host uses the plugin's translation files.

Return both met and unmet entries. The host calls this after loading and again
on demand, off the startup path. Keep the check quick and non-throwing; a failed
call is logged and treated as no reported requirements. Unmet entries produce
**Needs attention** in Plugins, a dismissible main-window notice, and warnings
in Linux Diagnostics. **Check again** and each Doctor run re-evaluate them.
Provide a repair hint; the host does not install external dependencies for you.

## Folder side strips

A folder provider can also implement `IFolderSideStripProvider` with
`ISideStripSession? CreateSideStripSession(SideStripContext context)`. The host
creates one session per side when that folder becomes visible, including on
return from a subfolder, and disposes it when another view or takeover owns the
strips. A newly visible folder gets fresh sessions. Return `null` to let the
host show plain dial labels/values or a blank strip.

These are normal strip sessions: whole-strip rendering, optional
`ISegmentStripSession`, animation, change events, taps, and swipes work as outside
a folder. `context.Rotaries` describes the folder's overrides rather than saved
command strings. Paging callbacks do nothing because rotary pages stay frozen
while a folder is open; the overrides still handle the dials.

For the standard host indicator, set `RotaryOverride.Label` and its optional
`Func<AdjustmentValue?> GetValue`. Return a cached value quickly and without
side effects; reads happen outside the render lock. Turning an override repaints
the strip; raise the folder's `EntriesChanged` when external data changes.

## CT wheel rendering and input

A command bound to the active CT wheel mode can implement `IWheelRenderer`:

```csharp
TimeSpan Interval { get; }
bool RenderWheel(IRenderCanvas canvas, WheelRenderContext context);
```

The host caps polling at 30 fps; `TimeSpan.Zero` draws only on demand.
`WheelRenderContext` contains `Command`, `Size`, `Label`, and `Value`. Derive
coordinates from `Size`: the canvas is square, but only its inscribed circle is
visible. Draw quickly from cached data; the cleared canvas is valid only during
the call. Returning `false` or throwing restores the user's wheel canvas or the
automatic label/value look. Devices without a wheel never call the renderer.

For raw gestures, implement `IWheelInputHandler.OnWheelTouch(WheelTouch touch,
CommandContext context)` and `OnWheelSwiped(WheelSwipeDirection direction,
CommandContext context)`. `WheelTouch` contains phase (`Start`, `Move`, `End`),
canvas-space X/Y, and finger id. Both methods return `bool`. Claiming any phase
of a touch suppresses its normal tap/swipe/menu action; claiming only a
classified swipe suppresses the corresponding workspace or mode-menu action.
The callbacks run on the device input thread and must not block. They are not
called while the host's mode menu, wake handling, or another owner consumes input.

Exclusive providers gain `RenderWheel(IRenderCanvas)`, `OnWheelTouch`, and
`OnWheelSwiped`. Use `ExclusiveControlScope.Wheel` to own the wheel independently.
`WheelFollowsRotaryScope` defaults to `true` for compatibility; set it to `false`
when rotary flags should own only the side dials. The CT wheel's rotary index is
6. Older hosts ignore these optional APIs, so keep a usable value/label fallback.

## Plugin tool and architecture-aware packaging

The .NET 10 `LoupixDeck.PluginTool` package provides `loupix new <Name>`,
`loupix pack [--project <dir>] [-o <dir>]`, and
`loupix install [--project <dir>] [--debug]`. Packing validates `plugin.json`
and builds the ZIP, manifest, and `SHA256SUMS`. Installing packs and copies a
development build to the user's plugin directory; restart the host afterwards.
The tool itself has native builds for Windows/Linux x64 and macOS x64/ARM64.

In SDK 1.33.0 tooling, a manifest may include `"architectures": ["x64"]` or
`["arm64"]` (or both); absent/empty means unrestricted. Keep this consistent with
native dependencies. The Store checks the process architecture, shows a reason
for an installed mismatch, and picks a matching published package. The reusable
workflow's `runtimes` input, such as `win-x64 win-arm64`, builds separate packages
with matching manifest restrictions. See [Packaging & Distribution](Packaging-and-Distribution)
for catalogue fields and release assets. Existing portable plugins need no rebuild.
