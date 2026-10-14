# Folder Redraw Probe — Measuring the Folder Redraw Path

Diagnostic tooling added for issue #440. It times every phase of a folder redraw (building the
entries, rendering the tiles, sending the grid, redrawing the side strips) so a slow folder can be
pinned on rendering or on the transfer to the device. It is off by default and costs nothing
until it is switched on.

## Parts

| File | Role |
|---|---|
| `LoupixDeck/Services/Diagnostics/FolderRedrawProbe.cs` | Collects the timings. While on, prints per phase the sample count, average, 95th percentile and maximum every 5 seconds. |
| `LoupixDeck/Commands/FolderStressTestCommand.cs` | Hidden command `System.FolderStressTest` that opens a synthetic folder and drives it at a fixed rate, or switches the probe on for a real folder. |
| Call sites | `LoupedeckLiveSController` (`RedrawFolderState`, `DrawFolderGrid`, `DrawSideStrip`), `FolderNavigationService.OnProviderEntriesChanged`, `PluginFolderAdapter.BuildEntries`. |

## The command

```
System.FolderStressTest({Hz},{Mode},{IconSize})      defaults: 10,dials,256
```

- `Hz` — how often the folder raises `EntriesChanged`. `0` opens nothing and only switches the probe
  on, to measure a real plugin folder: run it, open the folder on the device, use it.
- `Mode` — `dials`: only the dial values change, the keys stay identical (the case from #440).
  `keys`: the key text changes on every tick as well, so the grid has to be sent each time.
- `IconSize` — edge length of the PNG icon on every key. Large icons make decoding and scaling
  visible; the device key size (74–90 px) shows the floor.

Running the command a second time stops the test (closes the folder, or switches the probe off).
Leaving the folder on the device stops it too.

The command is hidden, so it does not appear in the command picker. Send it through the CLI.

## Running it (Windows)

Use a Release build: it writes the console to `~/.config/LoupixDeck/loupixdeck-startup.log`. A Debug
build on Windows writes to its own console window instead.

```bash
cd LoupixDeck && dotnet build LoupixDeck.csproj -c Release
cd bin/Release/net10.0 && ./LoupixDeck.exe &

# start, wait at least two report windows, read, stop
./LoupixDeck.exe --device razer-stream-controller "System.FolderStressTest(10,dials,256)"
sleep 12
grep -a "FolderProbe" ~/.config/LoupixDeck/loupixdeck-startup.log | tail -14
./LoupixDeck.exe --device razer-stream-controller "System.FolderStressTest(10,dials,256)"
```

Things to know:

- **Pick the device with `--device <slug|serial>`.** Without it the command goes to the primary
  device, which with several devices connected is easy to mistake for another one. Slugs are the
  config file names: `razer-stream-controller`, `razer-stream-controller-x`, `loupedeck-live-s`, …
- **Measure one device at a time.** The probe is static, so samples from two devices running the
  test at once land in the same statistics.
- **Only send CLI commands while an instance is running.** If none is, `./LoupixDeck.exe "<command>"`
  becomes the main instance itself and blocks the shell until it exits.
- The second instance that forwards a command recreates the startup log (`FileMode.Create`). The
  running instance keeps writing into the new file, so the probe lines still land there.
- Discard the first report window after starting: it contains the one-off full send on opening.

## Reading the output

```
[FolderProbe] 5,0s: events 10,0/s, redraws 10,0/s
[FolderProbe]     plugin build n=  50  avg=   0,00 ms ...
[FolderProbe]     png decode   n=  50  avg=   2,16 ms ...
[FolderProbe]   render tiles   n=  50  avg=   3,19 ms ...
[FolderProbe]   compose grid   n=  50  avg=   0,09 ms ...
[FolderProbe]     send grid    n=  50  avg=  16,38 ms ...
[FolderProbe]   grid hash+send n=  50  avg=  16,40 ms ...
[FolderProbe]     strip render n= 100  avg=   0,22 ms ...
[FolderProbe]     strip send   n= 100  avg=   3,11 ms ...
[FolderProbe]   strips (both)  n=  50  avg=   6,65 ms ...
[FolderProbe]   total redraw   n=  50  avg=  26,32 ms ...
[FolderProbe]   build entries  n=  50  avg=   2,17 ms ...
```

- `events` vs. `redraws`: announcements from the provider vs. redraws that actually ran. Fewer
  redraws than events means the redraws were coalesced because they could not keep up.
- `build entries` covers `plugin build` (the provider's `BuildEntries`) and `png decode` (converting
  the entries, which decodes their images). It runs before the redraw and is not part of `total redraw`.
- `grid hash+send` is the grid step as a whole; `send grid` only counts the sends that were not
  skipped. A `send grid` count far below `grid hash+send` means the unchanged-grid skip is working.
- `strips (both)` includes waiting for the per-strip frame floor (`StripMinRedrawMs`, 33 ms). Above
  ~30 Hz it grows by that wait, not by sending cost; `strip send` shows the real transfer.

## Results from #440 (2026-10-14)

Release build, 10 Hz, `dials`, 256 px icons, before the unchanged-grid skip:

| Device | Grid region | Send grid | Strips (both) | Total |
|---|---|---|---|---|
| Razer Stream Controller | 360×270 | 16.4 ms | 6.7 ms | 26.3 ms |
| Razer Stream Controller X | 472×270 | 23.9 ms | – | 26.8 ms |
| Loupedeck Live S | 450×270 | 44.9 ms | – | 48.9 ms |

Sending was 62–92 % of every redraw, and it does not depend on the icon size. The Razer devices
transfer about 11–12 MB/s, the Live S about 5.4 MB/s. With the skip in place a dial-only redraw
takes 3.4 ms (SC X), 9.9 ms (SC, mostly the strips) and 4.0 ms (Live S).
