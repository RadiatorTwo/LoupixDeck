# Plugin Screensavers and Animated Side Strips

SDK 1.20.0 adds two optional animation capabilities. They are additive, so
existing SDK 1.x plugins continue to load without a rebuild.

## Plugin screensavers

Implement `IScreensaverProvider` and return it from
`LoupixPlugin.GetScreensaverProviders()`.

A provider is a stateless factory with a stable `Id`, a picker `Title` and a
`CreateRenderer()` method returning a fresh
[`IFullDisplayRenderer`](Advanced-Full-Display-Renderer) for each idle run.
Register it from `GetScreensaverProviders()`.

The user selects `Plugin` and the provider in `Settings > Screensaver`. When the
idle timeout expires, the host creates the renderer, calls `OnStart(surface)`,
schedules `RenderFrame(...)`, and calls `OnStop()` when input, a context change,
plugin unload, or shutdown ends the screensaver. Returning
`FullDisplayFrameResult.Final(frameNumber)` ends a one-shot animation and
restores the active page. Plugin screensavers do not need `ffmpeg`: they supply
their own BGRA frames.

Keep per-run decoding, timing and resources in the renderer and release them
from `OnStop()`. The [Full-Display Renderer](Advanced-Full-Display-Renderer)
page describes frame buffers, dirty keys and scheduler timing.

## Side-strip providers

An `ISideStripProvider` creates an `ISideStripSession` for one side of one
rotary page. Return providers from `GetSideStripProviders()`. Users select a
provider in a side strip's `PluginOverride` mode. `SideStripContext` identifies
the left or right side, provides its geometry and rotary bindings, and has
side-specific page callbacks.

The host disposes the session when the user leaves its page or binding, turns
the device off, starts a full-device takeover, unloads the plugin, or exits.
Release timers, subscriptions and external resources in `Dispose()`. Static
sessions keep using `StripChanged` for rate-limited redraws. FreeDraw animated
image layers are a separate built-in path and are unchanged.

## Animated side-strip sessions

Implement `IAnimatedSideStripSession` on the same object as
`ISideStripSession`. Its `TargetFps` asks the central scheduler for a rate;
`<= 0` uses the host default and the effective rate can be lower. Use
`frame.EffectiveFps`, `Elapsed`, and `Delta` instead of counting ticks.
Rendering runs off the UI thread and must be fast and synchronous.
`RenderStrip()` remains required for authoritative repaints when attaching,
changing pages, or returning from another display owner.

Return `AnimationFrameInfo.Skip()` when nothing changed,
`AnimationFrameInfo.Frame(number)` for a continuing frame, or
`AnimationFrameInfo.Final(number)` for the last frame. The number is a monotonic
dirty key; reusing it avoids unnecessary device writes. For animated sessions,
raising `StripChanged` requests the next frame immediately and resumes a
session that returned `Final`.

The host paces left and right strips independently on its shared scheduler. An
in-flight render may overlap `Dispose()`, but no new one starts after detach, so
guard state that disposal tears down.
