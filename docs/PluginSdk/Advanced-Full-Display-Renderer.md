# Full-Display Renderer

A *full-display renderer* streams raw framebuffers to the **entire device
display**, driven by the host's central animation scheduler. Use it for
continuously animated full-screen content: a video, a visualizer, a telemetry
dashboard that redraws every frame.

It is the high-throughput sibling of [exclusive mode](Advanced-Exclusive-Mode):

| | Exclusive mode | Full-display renderer |
|---|---|---|
| You deliver | Per-slot [`FolderEntry`](Advanced-Folders#folderentry) tiles (text/PNG) | One contiguous BGRA framebuffer per frame |
| Host does | Decode + composite + push | Push |
| Input | Every hardware input is routed to you | Buttons are inert; **any press ends the takeover** |
| Best for | HUDs, dashboards, menus, anything interactive | Video, visualizers, continuous animation |

Both take the whole display, so they are **mutually exclusive**: whoever owns it
first wins, and neither steals from the other.

## Requesting the display

```csharp
IFullDisplayRenderSession? session = host.RequestFullDisplayRenderer(myRenderer);
if (session == null)
{
    // Display already owned (another full-display renderer or exclusive mode),
    // no active device, or your OnStart threw. Handle it — don't assume success.
    return;
}

_session = session;   // keep it; releasing it is how you give the display back
```

The returned `IFullDisplayRenderSession` **is** the ownership handle:

```csharp
public interface IFullDisplayRenderSession : IDisposable
{
    bool IsActive { get; }   // false once released or revoked by the host
    void Release();          // idempotent; same as Dispose()
}
```

Release it when you're done — and **always release it in `Shutdown()`**, so an
unload tears your renderer down deterministically.

## IFullDisplayRenderer

```csharp
public interface IFullDisplayRenderer
{
    int  TargetFps { get; }
    bool IsActive  { get; }

    void OnStart(FullDisplaySurface surface);
    void OnStop();

    FullDisplayFrameResult RenderFrame(byte[] buffer, in FullDisplayFrameContext frame);
}
```

| Member | Notes |
|---|---|
| `TargetFps` | Desired frame rate. The host raises its global animation cap to this value for the duration of the session; `<= 0` means "use the host default". Read `frame.EffectiveFps` for the rate you actually get. |
| `IsActive` | Polled every tick. Returning `false` pauses frame pulls cheaply **without** releasing the session — use it when your producer has nothing to show. |
| `OnStart(surface)` | Called once, off the UI thread, when the session is granted, with the exact target geometry. Start decoding/producing here. Throwing here fails the request (`RequestFullDisplayRenderer` returns `null`). |
| `OnStop()` | Called once when the session is released — by you, by a profile switch, by a button press, on plugin unload, or at host teardown. Stop decoding and free resources. |
| `RenderFrame(buffer, frame)` | Fill `buffer` with one frame. Called off the UI thread at up to `TargetFps`. |

### The frame buffer

```csharp
public readonly struct FullDisplaySurface
{
    public int Width  { get; init; }   // device pixels
    public int Height { get; init; }
    public int Stride { get; init; }   // bytes per row — Width * 4 for BGRA
    public FullDisplayPixelFormat PixelFormat { get; init; }   // Bgra8888 today
}
```

The `buffer` handed to `RenderFrame` is **host-owned and pooled**:

- It holds *at least* `Stride * Height` bytes. Write exactly that many.
- **Never read `buffer.Length`** — a pooled array may be larger than the frame.
- Don't keep a reference to it after `RenderFrame` returns; it goes back to the
  pool.

### RenderFrame must be fast

`RenderFrame` runs on the scheduler thread. Decode/produce **asynchronously** on
your own thread and just copy the freshest frame here — a blocking call stalls
every other animation on the device.

Report what you did with `FullDisplayFrameResult`:

```csharp
FullDisplayFrameResult.Skip()               // nothing new this tick — host leaves the display alone
FullDisplayFrameResult.Frame(frameNumber)   // wrote a frame; stream continues
FullDisplayFrameResult.Final(frameNumber)   // wrote the last frame; host stops ticking and holds it
```

`frameNumber` is a **monotonic dirty key**: the host only pushes to the device
when it differs from the last pushed value, so repeating a number costs no
serial I/O. After `Final` the session stays open (the last frame stays on
screen) until you release it.

### Timing

```csharp
public readonly struct FullDisplayFrameContext
{
    public long              FrameNumber       { get; init; }   // since ticking started
    public TimeSpan          Elapsed           { get; init; }
    public TimeSpan          Delta             { get; init; }   // since the previous frame
    public int               EffectiveFps      { get; init; }   // after host clamping
    public FullDisplaySurface Surface          { get; init; }
    public CancellationToken CancellationToken { get; init; }   // cancelled on host teardown
}
```

Drive your output from `Elapsed` / `Delta` rather than counting ticks — the
effective rate can be lower than `TargetFps`.

## Example: streaming decoded frames

```csharp
public sealed class VideoRenderer : IFullDisplayRenderer
{
    private readonly object _gate = new();
    private byte[]? _latest;          // produced by the decoder thread
    private long _produced;
    private long _handedOut = -1;

    public int TargetFps => 30;
    public bool IsActive => true;

    public void OnStart(FullDisplaySurface surface)
    {
        // Start the decoder, scaling to surface.Width x surface.Height, BGRA.
        // Each decoded frame: lock (_gate) { _latest = bytes; _produced++; }
    }

    public void OnStop()
    {
        // Stop the decoder and free its buffers. Must be safe to call at any time.
    }

    public FullDisplayFrameResult RenderFrame(byte[] buffer, in FullDisplayFrameContext frame)
    {
        lock (_gate)
        {
            if (_latest == null || _produced == _handedOut)
                return FullDisplayFrameResult.Skip();          // nothing new — no device write

            Buffer.BlockCopy(_latest, 0, buffer, 0, frame.Surface.Stride * frame.Surface.Height);
            _handedOut = _produced;
        }

        return FullDisplayFrameResult.Frame(_handedOut);
    }
}
```

Wire it to a command and release it on shutdown:

```csharp
[Command("video.start", "Start Video", "Media")]
public sealed class StartVideoCommand : IPluginCommand
{
    public void Execute(CommandContext context) => MyPlugin.Instance.StartVideo();
}

public override void Shutdown()
{
    _session?.Release();      // idempotent — safe even if it already ended
    _session = null;
}
```

## Notes

- **Single owner.** `RequestFullDisplayRenderer` returns `null` when the display
  is already taken (another renderer or exclusive mode) — handle it.
- **Start it from a command, not automatically.** Same reasoning as exclusive
  mode: entering on load would have two plugins fighting over the device.
- **The device is fully taken over.** Buttons, touch and rotaries have no normal
  function while you render, and **any press ends the session** (the host calls
  `OnStop` and repaints the page) — that is the user's way out, so you don't have
  to wire an exit yourself.
- **A profile/workspace switch ends the takeover**, exactly like exclusive mode.
  It does **not** auto-restart; the user re-runs your start command.
- **Turning the device off pauses, it doesn't release.** The host stops pulling
  frames and resumes on the next power-on, so keep your producer warm across
  `IsActive == false` / paused stretches instead of tearing it down.
- **Unload is covered.** The host force-releases any session your plugin still
  holds after `Shutdown()` returns, so a renderer never keeps ticking inside an
  unloaded plugin — but release it yourself so `OnStop` runs while your code is
  still fully alive.
- **Keep `OnStop` cheap and idempotent.** It can arrive from a device thread, a
  profile switch, or shutdown. Don't try to re-enter from inside it.
