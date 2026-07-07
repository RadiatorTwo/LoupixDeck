# Native Haptic Feedback — Protocol Notes & Constraints

> **Status (2026-07): the firmware-side native haptic (`0x2e`) below is NOT used.**
> Haptic feedback now runs entirely through the software `Vibrate()` pulse (`0x1b`),
> driven from the touch handler (`LoupedeckLiveSController.ResolveVibrationPattern` +
> `OnTouchButtonPress`). A per-button "Vibration enabled" override wins; otherwise the
> global pattern's first effect (Settings → Feedback, `HapticSteps[0].Effect`) applies
> to every button, and it fires **immediately on touch** (no press-and-hold).
>
> Why `0x2e` was dropped (all reproduced live on the device, wire-logged):
> - Byte-correct frames — identical to the `#103` implementation and to Athorus'
>   reference from issue #101 — produced **no vibration**, with or without the
>   `0x19` strength "arm", as single combined frames or one-frame-per-button.
> - The `0x2e` **disable** command (`[0x4d,0x01]`) **wedges / freezes** the firmware
>   haptic engine for the session (also reported in issue #101). It is no longer sent.
> - `0x2e` only fires an effect *after* a delay while the finger is **held** — poor UX
>   for tap feedback. `0x1b` fires instantly.
>
> `NativeHapticService` is kept for DI but performs no device I/O. The protocol notes
> below are retained for reference in case the firmware path is revisited.
>
> Limitation: the software path plays a single effect, so the global pattern's
> *Delay* / *Duration* / *second effect* (a firmware multi-step concept) have no effect;
> simplifying that Settings UI is a possible follow-up.

Reverse-engineered firmware-side haptic for the Loupedeck Live S, based on
notes from Athorus (GitHub issue) plus my own empirical testing on my device.

## Op-codes

All payloads below are the **data** portion — the serial wrapper
(`[length, cmd, txId]`) is added by `LoupedeckDevice.SendNoResponse`.

### `0x2e` — SET_HAPTIC (per-button slot config)

Enable payload:
```
[screen, 0x00, count, (btn, seq, fx, delay, dur) * count]
```
- `screen` — `0x4d` (center touch display).
- `count` — number of slot entries (one byte).
- Each slot: 5 bytes — touch-key id, sequence index, DRV2605 effect id, delay, duration.

Disable payload:
```
[screen, 0x01]
```

### `0x19` — SET_HAPTIC_STRENGTH (global)

```
[0x02, 0x03, 0x00, 0x0a, strength]
```
- `strength` — `0x00`..`0x04` per Athorus.
- **Has no audible/tactile effect on my device**: I tested 0x00..0x04 with the
  same slot config and could not perceive any change in vibration intensity.
  I kept the low-level `LoupedeckDevice.SetHapticStrength` wrapper for
  reference, but `NativeHapticService` no longer calls it and the settings UI
  has no Strength slider.
- The display-freeze on disable I initially attributed to "missing strength
  init" — that freeze was actually the firmware crash described under
  *Per-button sequence limit* below. Sending strength does not influence it.

## Slot fields

| Field    | Range / unit |
|----------|-------------|
| `btn`    | `0x00`..`0x0e` — own index space for the 15 touch keys (not the same as the hardware-button ids in `Constants.Buttons`). |
| `seq`    | `0x00`..N — sequence index per button, all effects fire relative to touch-start (not to the prior effect). |
| `fx`     | DRV2605 library effect id, see `Constants.VibrationPattern`. |
| `delay`  | Time before the effect fires (unit ≈ 10 ms ticks, empirical). **Minimum 0x04** — lower values cause the firmware to silently drop subsequent sequence entries. |
| `dur`    | Effect duration. **Minimum 0x02** — lower values cause the firmware to play a default effect instead. |

I always set the first effect of a sequence to `delay = 0x04` so it fires
immediately — anything lower feels identical to me, and < 4 breaks the rest
of the sequence.

## Per-button sequence limit

In my testing the firmware accepts **at most 2 `seq` entries per touch
button**. Sending a 3rd `seq` for any button reliably crashed my device. I
capped the user-facing sequence length at 2 and hide the "Add second effect"
button once both slots are present.

## Frame-size / firmware limits

- A single `EnableNativeHaptic` frame with **30 slots** (15 buttons × 2 steps)
  crashed my device. 15 slots (15 buttons × 1 step) worked. Athorus' reference
  frames are 2–3 slots.
- To stay safe I send **one frame per button** — each frame contains only that
  button's slots. With N steps per button, the largest frame is `3 + 5*N` bytes
  of payload. Frames are serialized through the existing send queue.
- The on-the-wire length byte caps at `0xff` regardless, so a single oversized
  frame would also misframe.

## Slot replacement semantics

`Enable` with a per-button slot list does **not** clear unspecified `seq`
entries for that button. Reducing a sequence from 2 steps to 1 leaves the old
`seq=1` slot live in firmware until either:

1. `Disable` is sent (clears everything), or
2. A new `Enable` overwrites the same `(btn, seq)` key.

`NativeHapticService.SendNow` therefore sends `Disable` before the cascade of
15 per-button `Enable` frames on every reapply. That is the cheapest reliable
way I found to handle removals.

## Co-existence with the legacy software vibrate (`0x1b`)

I removed the legacy `Vibrate(pattern)` path from `OnTouchButtonPress`. If it
gets reintroduced, note that it fires per touch *in addition* to native haptic
and will cause double-vibration on buttons that still have
`VibrationEnabled = true` in their per-button config.

## Recovery from a frozen device

A bad frame can wedge the display. Power-cycling the device (unplug USB) and
relaunching the app applies the persisted config cleanly via the startup
`INativeHapticService.Apply()` call.
