# Animated wallpaper (video background behind the touch grid)

Branch: `feature/animated-wallpaper` (from `origin/master` @ `721d0c5`)

## Goal

A page wallpaper may be a video clip instead of a still image. The clip plays continuously
behind the touch buttons — not only while idle, as the screensaver does — and loops
seamlessly. Length is not bounded: a short animated background and a full-length film are
both valid inputs.

## Decisions already taken

| Topic | Decision |
|---|---|
| Decode | Runtime ffmpeg stream, not a pre-decoded frame budget |
| Loop | Hybrid — short clip decodes into a RAM frame ring (seamless), long clip streams |
| Side displays | Covered by the panel-wide clip; a side slot holding a still image wins over the video |
| Per-side video | Not supported. A side display never plays its own clip |
| Button animations | Composite into the panel frame; no more per-button partial pushes while a video wallpaper runs |
| Playback position | Not persisted. Restart begins at 0 |
| Source file | Path reference on the slot, like `ScreensaverVideoPath` — never copied into the asset store |

## Measured baseline

Razer Stream Controller, 480×270 panel, synthetic 1080p30 source, `LOUPIX_SS_DEBUG=1`,
~66 s undisturbed, all three decks connected but only this one animating:

- First frame after **89 ms**
- Device push per frame **26.9 / 27.1 / 28.0 ms** (min / median / max) — remarkably stable
- **30.0 fps sustained** in 60 of 65 reporting windows, zero drops
- ffmpeg at `speed=0.999x`, queue wait 0–6 ms

Two conclusions that shape this plan:

1. **The serial panel write is the floor, at ~27 ms.** No software change moves it. At 30 fps
   the frame budget is 33.3 ms, leaving **≈6 ms per frame** for compositing. The overlay work
   must therefore be blits of pre-rendered bitmaps — never layer rendering, text layout or
   plugin callbacks inside the frame path.
2. **`-stream_loop -1` is not seamless.** Every loop the demuxer hits EOF and the decoder is
   flushed and re-initialised (`Reinit context to 1920x1088`), costing ~0.5 s: 14 dropped
   frames and a dip to ~20 fps at each seam. Measured three times, exactly on the seams.
   This is why the loop strategy is hybrid.

## Non-goals

- Audio. ffmpeg runs with `-an`.
- Per-side-display video sources.
- Fixing the screensaver's own loop-seam hitch. It has the same defect today; that is a
  separate concern and gets its own commit or issue (see "Follow-ups").

## Architecture

### Frame path per rendered frame

```
VideoFrameStream ──► BGRA panel frame
                          │
                          ▼
        FullDisplayFrameWriter.PushAsync(frame, overlay:)
                          │
                overlay callback, under the Skia gate:
                  1. dim rect (slot Opacity)
                  2. static side-slot images over their columns
                  3. cached key foregrounds at GetWallpaperKeyRect(i)
                          │
                          ▼
              slice per display ──► atomic FRAMEBUFF + DRAW
```

### Components

**`VideoFrameStream` (new, extracted)** — the ffmpeg pipe currently embedded in
`ScreensaverAnimationSource`: process spawn, `-re` wall-clock pacing, bounded channel at
`FrameQueueDepth = 6`, drop-to-freshest on a lagging consumer, stderr drain, debug counters.
The hard-won argument layout moves with it verbatim, comments included — in particular
`-analyzeduration 0` + small `-probesize` for startup latency, and the standing warning never
to add `-fflags nobuffer`.

Two loop modes behind one interface:

- **Ring mode** (short clip): decode the whole clip once into an `SKBitmap[]` frame ring at
  panel size, then replay from RAM. No demuxer restart, so the seam is genuinely seamless.
  Bounded by a frame-count/byte budget — at 480×270×4 ≈ 518 KB per frame, a 5 s clip at
  15 fps is ≈39 MB, which is the right order of magnitude for the cap. Exceeding the budget
  falls back to stream mode.
- **Stream mode** (long clip): today's behaviour, `-stream_loop -1`. One seam at the end of a
  long clip is acceptable; a seam every three seconds is not.

Mode selection happens once at start, from `ffprobe` duration and the byte budget.

**`FullDisplayFrameWriter` (modified)** — `PushAsync` gains an optional
`Action<SKCanvas>` invoked on the composed panel bitmap *before* slicing, inside the existing
`lock (SkiaRenderGate.Sync)` block. Everything else is untouched: the single atomic
full-screen blit + DRAW, the CT column slicing, the `_idle` gate that keeps `Dispose` from
cutting a serial write mid-stream. One transfer path stays one transfer path.

**Key foreground cache (new)** — each touch button rendered once onto a transparent
background and cached; invalidated only on real content change (layer edit, page change,
press state, animated-layer frame advance). ~15 × 90×90×4 ≈ 0.5 MB, negligible. This
requires splitting `BitmapHelper.RenderTouchButtonContent` (`Utils/BitmapHelper.cs:1810`)
into a wallpaper/dim part and a layers part; the existing single-bitmap entry point stays as
a thin composition of the two so no current caller changes behaviour.

`device.GetWallpaperKeyRect(index)` (`LoupedeckDevice/Device/LoupedeckDevice.cs:80`) already
gives each key's rectangle in panel space — the exact inverse of today's cutout lookup, and
the reason the grid offset stays correct on Razer and CT where the grid sits 60 px in.

**`WallpaperAnimationSource` (new, `IAnimationSource`)** — pulls a frame from the
`VideoFrameStream`, pushes it through the writer with the overlay callback. Mirrors
`ScreensaverAnimationSource`'s lifecycle, including the `ManualResetEventSlim` idle gate.

**`WallpaperAnimationManager` (new)** — mirrors `ButtonAnimationManager`: subscribes to
`IPageManager.OnTouchPageChanged` to (re)build the source for the active page, and gates
`IsActive` on the same conditions the other managers already respect —
`IScreensaverManager.Started/Stopped`, `IExclusiveModeService.IsActive`,
`IFullDisplayRenderService.IsActive`, `IFolderNavigationService.IsActive` — plus the swipe
animators (`TouchPageAnimator`, `StripSwipeAnimator`), which own the panel while they run.

### Side displays

`BitmapHelper.ResolveSideWallpaper` (`:633`) already returns `(null, 0)` when a side slot has
no image of its own, and callers then fall back to the main wallpaper's panel region. That is
exactly the required precedence — the video is the main slot, and a side slot with a still
image draws over its column in the overlay. No new precedence rule is introduced.

On unified devices (Live S, Razer) the strips are columns of the same panel and cost nothing
extra. On the CT they are separate buffers the writer already slices, and each one is an
additional serial write per frame — so the CT's achievable frame rate will be lower. Measure
before promising a number.

### Button animations

While a video wallpaper is running, animated button layers must not push individually via
`DrawTouchButton` — a partial write racing a full-panel push tears. Instead
`ButtonAnimationSource` advances its `DecodedAnimation` frames into the foreground cache and
lets the wallpaper source push. Their frames are already plain blits, so this is cheap and it
*removes* an exclusion rather than adding one: animated buttons and animated wallpaper can run
together. Their effective rate becomes the panel rate.

When no video wallpaper is active, the current per-button partial-push path stays exactly as
it is.

## Configuration and compatibility

`WallpaperSlot` gains optional fields, defaulted so an old `config.json` is bit-for-bit
unaffected:

| Field | Type | Default | Note |
|---|---|---|---|
| `VideoPath` | `string` | `null` | Absolute path, not asset-store relative |
| `VideoName` | `string` | `null` | Display name for the settings UI |
| `VideoFps` | `int` | `30` | Clamped by the scheduler's global limit |

Absent from an old file → `null`/default → `HasVideo` false → today's static behaviour, no
migration needed. `AssetPath` keeps its meaning and its JSON name unchanged. Generated JSON
property names must match these hand-written names exactly.

`HasVideo` takes precedence over `HasImage` on the same slot; the still image stays stored so
removing the video restores it.

Without ffmpeg on `PATH` (`FfmpegDetector.IsAvailable()`), a slot with a video falls back to
its still image, or to no wallpaper — logged once, never an error dialog.

## Status

| Phase | State | Commit |
|---|---|---|
| 1 — Extract `VideoFrameStream` | done | `acfbbad` |
| 2 — Seamless ring loop | done | `1b2a124` |
| 3 — Overlay hook in `FullDisplayFrameWriter` | done | `85eaf17` |
| 4 — Split `RenderTouchButtonContent` | done | `f15b9b3` |
| 5 — Playback (model, cache, source, manager, DI) | done | `43e5f42`, `609cbac` |
| 6 — Button animations into the foreground cache | done | this commit |
| 7 — Settings UI | done | this commit |

Nothing is pushed. Branch `feature/animated-wallpaper`, based on `origin/master` @ `721d0c5`.

### What the phases actually cost, measured

All on a Razer Stream Controller (480×270 panel), all three decks connected but only this one
animating, `LOUPIX_SS_DEBUG=1` / `LOUPIX_WP_DEBUG=1`.

| | |
|---|---|
| Serial panel write | 26.0–27.7 ms per frame, remarkably stable across every run |
| Overlay (dim + 14 keys × 2 text layers) | **0.1 ms** median, 0.2 ms worst |
| Sustained rate with video wallpaper | 30.0 fps in 39 of 41 windows |
| Ring prefill, 3 s clip | 90 frames in 142 ms; first frame at 90 ms |

The plan's headline risk — that ~6 ms of compositing headroom might not be enough — is settled:
the overlay uses about a sixtieth of it. The serial write is the only limit, and it is a floor no
software change moves.

## Phases

### Phase 1 — Extract `VideoFrameStream` (pure refactor) — done

Verified against the pre-refactor baseline: first frame 89 → 85 ms, push avg 26.9/27.1/28.0 →
27.0/27.3/27.5 ms, 42 dropped frames in both, on the same three loop seams.

### Phase 2 — Seamless ring loop — done

A short looping clip is decoded once without `-re` into a RAM frame ring; ffmpeg then exits.
Guards: `ffprobe` duration ≤ 15 s and ≤ 96 MB per stream, both verified to fall back correctly
(a 10 s clip reports `301 frames would need 149 MB`, a 20 s clip reports the seconds cap).

**Correction to the verification criterion as originally written.** "No `Reinit context` line" was
too crude: ffmpeg emits two of those while it initialises its decoder during the one-and-only
decode pass. The criterion that actually distinguishes ring from streaming is **no ffmpeg activity
at all after `ring filled`** — no EOF, no decoder reset, no reinit. That holds, and the ~20 fps
seam windows disappear entirely.

### Phase 3 — Overlay hook in `FullDisplayFrameWriter` — done

Optional `Action<SKCanvas>` before slicing. The callback is wrapped in try/catch: an exception
escaping it would unwind past the disposal in the `finally` and leak the frame's native pixels on
every throw.

Note the plan called for building both platforms here. There is no `#if` in this file, so there was
no platform-gated path to build; only Linux was built.

### Phase 4 — Split `RenderTouchButtonContent` — done

`DrawTouchButtonBackground` + `RenderTouchButtonForeground`; `RenderTouchButtonContent` issues the
same calls in the same order, so existing rendering is unchanged by construction.

**Correction to "pixel-identical".** Compositing a separately rendered foreground is equal, not
bit-equal: the premultiplied intermediate rounds twice. Measured over eight synthetic cases on a
90×90 key, five were bit-identical and three differed by at most 1/255 on antialiased edge pixels
where strokes overlap (17, 14 and 1 of 32400 bytes) — all of them outlined text. Invisible, but it
is not zero, and the equivalence holds only while the layer path stays pure source-over.

**Scope change: the foreground cache moved to Phase 5.** A cache is only as correct as its
invalidation, and the triggers live in paths Phase 6 touches — plugin layers and dynamic text
change unpredictably. Wiring invalidation blind, with no consumer, risked breaking live rendering
for users who never enable a video wallpaper. It now lives next to its only consumer.

### Phase 5 — Playback — done

`WallpaperSlot.VideoPath/VideoName/VideoFps` (`43e5f42`), then
`TouchButtonForegroundCache` + `WallpaperAnimationSource` + `WallpaperAnimationManager` + DI
(`609cbac`).

Backward compatibility verified: configs saved before the model change loaded with no error, and
re-saving altered no existing value — the diff is purely the three new fields at their defaults
(0 removed or changed lines across all three device configs).

Two deliberate behaviours, both documented in the source: a side slot holding a still image wins
over the video on its column; and the dim covers the whole panel rather than each key rectangle,
because a video fills the panel edge to edge and per-key dimming would leave bright seams in a
gapped grid.

### Phase 6 — Button animations into the foreground cache — done

`IWallpaperAnimationManager` exposes two guards rather than the bare invalidation hooks it had
before: `TryRedirectButtonRedraw(index)` and `TryRedirectPageRedraw()`. Each returns false when no
clip is playing — or while playback is paused for a screensaver, a takeover or folder navigation —
and the caller then does exactly the partial push it always did. When a clip *is* playing they drop
the key's (or every key's) cached foreground, request a frame, and return true, so the key is
re-rendered into the next video frame's overlay instead of racing it.

Redirected callers:

- `ButtonAnimationSource` — the animated image layers and animated plugin commands. Their layer
  mutation (`SetAnimationFrame` / `SetAnimationBitmap`) is unchanged; only the push is redirected,
  so animated buttons and an animated wallpaper now run together at the panel's rate.
- `LoupedeckLiveSController.TouchItemChanged` — the per-button path every content change funnels
  through (dynamic text, plugin state).
- The controller's three recurring whole-page repaints: `DrawCurrentTouchPageButtons`,
  the folder-mode-left restore, and the debounced `WallpaperInvalidated` handler.

One-shot restores that a video frame overwrites anyway are deliberately left alone — the touch
flash, `DeviceService.ShowTemporaryTextButton`, the benchmark repaint, and `PageManager`'s
page-change draw (the manager tears playback down and rebuilds on that event regardless). Guarding
`PageManager` or `DeviceService` would also introduce a DI cycle, since the manager depends on both.

*Verified* on the Razer Stream Controller with a 3 s clip and a 10 fps animated GIF layer on key 0
plus text layers on keys 1–3: 30.0 fps sustained across every measurement window, 0 dropped frames,
overlay avg 0.0–0.1 ms, and one redirect per GIF frame (240 redirects in ~25 s — i.e. every
animated-button push went through the overlay, none to the panel). With the clip removed from the
slot: zero redirects, no wallpaper source, no errors — the old path unchanged.

### Phase 7 — Settings UI — done

A "Video clip" section in the Edit Wallpaper dialog (`TouchPageWallpaperSettings`), below the image
picker and visible only while the **main** target is selected — a side display never plays its own
clip, so offering the control there would be a lie. It holds *Select video… / Clear*, the clip's
display name, an FPS spinner (1–60, disabled until a clip is chosen) and two hints: what the clip
does to the image and to the side displays, and — when the probe finds no ffmpeg on `PATH` — that a
slot with a clip falls back to its image until ffmpeg is installed. The probe runs off the UI thread
and assumes ffmpeg is present until it answers, so the hint never flashes on a machine that has it.

The clip is referenced in place and never imported into the asset store, matching the screensaver
rule: a clip may be arbitrarily large and ffmpeg reads it straight from disk. *Clear* drops only the
clip, so the still image behind it takes over again. Cancel already restored from a `Clone()`
snapshot via `CopyFrom`, both of which carry the video fields, so it reverts a clip too.

*Verified:* a `config.json` with every `VideoPath`/`VideoName`/`VideoFps` key removed — a genuine
pre-branch file — loaded with no error or warning, and the file it saved back differs only by those
nine keys reappearing at their defaults (`null`/`null`/`30`). No key lost, no value changed.

## Open risks

- **Multi-device contention.** Every measurement so far had exactly one deck animating. Three
  animating decks share the global Skia gate; the numbers will be worse. Not yet measured.
- **The CT.** Three separate buffer writes per frame instead of one, so likely the slowest device
  by a wide margin. May need its own default fps. Not yet measured — no CT is attached here.
- **Permanent ffmpeg process per device** on any page with a clip: CPU cost is the steady state,
  not the idle state. Ring mode removes it for short clips, which is the common case for an
  animated background.
- **Screensaver semantics.** An idle timeout is odd when the panel is never static. Product
  decision, little code: the screensaver still takes over (it is a different clip), or it is
  suppressed while a video wallpaper runs.
- **Visual confirmation obtained.** For most of this work every result came from instrumentation and
  logs only. That no longer holds: the Razer Stream Controller was watched while a clip played and
  showed the text layers "K1", "K2", "K3" standing legibly over the moving ffmpeg test pattern, and
  — in a second run with a key animation built for the purpose — key 0 showed its black background
  with a white bar sweeping left to right while the video kept playing behind the grid. That is
  Phase 6's whole point seen directly: an animated button and an animated wallpaper running at once,
  in one panel write. Still unseen: the Phase 7 settings dialog, and any deck other than this one.

## Follow-ups (separate commits / issues)

- The screensaver's own loop-seam hitch. Once Phase 2 exists, pointing the screensaver at
  ring mode for short clips is a small, isolated change — but it is a behaviour change to a
  different feature and does not belong in this branch.
- Wiki documentation, if any of this surfaces in the Plugin SDK. Docs go in
  `LoupixDeck.PluginSdk.wiki`, never into a `docs/` folder in a code repo.
