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
| 6 — Button animations into the foreground cache | open | — |
| 7 — Settings UI | open | — |

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

### Phase 6 — Button animations into the foreground cache — open

**This is also where the currently known gap gets closed.** Per-button pushes are not yet
redirected, so while a clip plays, a key whose content changes still writes itself directly via
`DrawTouchButton` and is then overwritten by the next video frame — visible as a flicker. And only
page changes invalidate cached foregrounds so far; `IWallpaperAnimationManager.InvalidateButton`
exists for this phase to call from wherever a key is re-rendered.

*Verify:* an animated button layer and a video wallpaper run together with no tearing; with the
video off, the old partial-push path is unchanged.

### Phase 7 — Settings UI — open

Video selection for the main slot, an fps control, and a clear disabled state explaining a missing
ffmpeg. English UI text throughout.

*Verify:* load a `config.json` saved before this branch, confirm identical behaviour, save, reload,
confirm no data loss.

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
- **No visual confirmation.** Every result here is from instrumentation and logs; the displays were
  never seen during this work.

## Follow-ups (separate commits / issues)

- The screensaver's own loop-seam hitch. Once Phase 2 exists, pointing the screensaver at
  ring mode for short clips is a small, isolated change — but it is a behaviour change to a
  different feature and does not belong in this branch.
- Wiki documentation, if any of this surfaces in the Plugin SDK. Docs go in
  `LoupixDeck.PluginSdk.wiki`, never into a `docs/` folder in a code repo.
