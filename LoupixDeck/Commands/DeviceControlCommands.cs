using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Commands;

[Command("System.DeviceOff", "Device OFF (blank display + LEDs)", "Device Control")]
public class DeviceOffCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.ClearDeviceState();
}

[Command("System.DeviceOn", "Device ON (restore from config)", "Device Control")]
public class DeviceOnCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.RestoreDeviceState();
}

[Command("System.DeviceToggle", "Device Toggle ON/OFF", "Device Control")]
public class DeviceToggleCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.ToggleDeviceState();
}

[Command("System.DeviceWakeup", "Device Wakeup (reconnect serial + ON)", "Device Control")]
public class DeviceWakeupCommand(IDeviceController controller, IDeviceService deviceService) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        try
        {
            deviceService.ReconnectDevice();
            await Task.Delay(500);
            await controller.RestoreDeviceState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Device wakeup failed: {ex.Message}");
        }
    }
}

[Command("System.ExclusiveStressTest", "Exclusive Mode FPS Stress Test (toggle)", "Device Control",
    parameterTemplate: "({Hz},{Mode})",
    parameterNames: ["Requests per second", "Mode (full/grid/dirty/tile)"],
    parameterTypes: [typeof(int), typeof(string)],
    Hidden = true)]
public class ExclusiveStressTestCommand(IExclusiveModeService exclusiveMode) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        // Toggle: if our own test is running, stop it. A different exclusive
        // provider is left untouched.
        if (exclusiveMode.Current is ExclusiveStressTestProvider running)
        {
            exclusiveMode.Exit(running);
            return Task.CompletedTask;
        }

        var hz = 120; // default: push faster than any sane cap to exercise coalescing
        if (parameters is { Length: > 0 } && int.TryParse(parameters[0], out var parsed) && parsed > 0)
            hz = parsed;

        // Mode maps directly to the SDK render strategy the controller uses:
        //   full  → full-screen atomic blit + DRAW (default baseline)
        //   grid  → every slot as its own 90x90 tile, no DRAW
        //   dirty → only changed tiles re-sent, no DRAW
        //   tile  → single 90x90 slot (14), no DRAW
        var modeStr = parameters is { Length: > 1 } ? parameters[1].Trim().ToLowerInvariant() : "full";
        var renderMode = modeStr switch
        {
            "grid" => PluginSdk.ExclusiveRenderMode.Grid,
            "dirty" or "dirtytiles" => PluginSdk.ExclusiveRenderMode.DirtyTiles,
            "tile" or "single" => PluginSdk.ExclusiveRenderMode.SingleTile,
            _ => PluginSdk.ExclusiveRenderMode.FullScreen
        };

        ExclusiveStressTestProvider provider = null;
        provider = new ExclusiveStressTestProvider(hz, () => exclusiveMode.Exit(provider), renderMode);
        if (!exclusiveMode.TryEnter(provider))
        {
            Console.WriteLine("Exclusive stress test: another exclusive provider is already active.");
            provider.Dispose();
        }
        else
        {
            Console.WriteLine($"[StressTest] {hz} Hz, mode={renderMode}");
        }
        return Task.CompletedTask;
    }
}

[Command("System.DrawBenchmark", "Benchmark raw device draw speed", "Device Control",
    parameterTemplate: "({Frames})",
    parameterNames: ["Frames"],
    parameterTypes: [typeof(int)],
    Hidden = true)]
public class DrawBenchmarkCommand(IDeviceService deviceService, LoupedeckConfig config) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        var frames = 100;
        if (parameters is { Length: > 0 } && int.TryParse(parameters[0], out var n) && n > 0)
            frames = n;

        var device = deviceService.Device;
        if (device == null)
        {
            Console.WriteLine("[Benchmark] no device connected.");
            return;
        }

        var slots = device.TouchButtonCount;
        Console.WriteLine($"[Benchmark] start — {frames} frames, {slots} slots (90x90), key blit");

        // Alternate two colours so the panel can't short-circuit identical frames.
        using var keyA = MakeSolid(90, 90, new SKColor(255, 0, 0));
        using var keyB = MakeSolid(90, 90, new SKColor(0, 0, 255));

        try
        {
            // Each variant is measured twice — WITH the trailing DRAW (0x0f) refresh
            // and WITHOUT it (FRAMEBUFF only) — so the cost of the per-frame DRAW is
            // directly visible. The official software streams frames without DRAW.

            // 1) Single 90x90 slot, WITH DRAW (FRAMEBUFF + DRAW round-trip).
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
                await device.DrawTouchSlot(0, (i & 1) == 0 ? keyA : keyB, refresh: true);
            sw.Stop();
            Report("single slot 90x90  [with DRAW]", frames, sw.Elapsed);

            // 1b) Single 90x90 slot, NO DRAW (FRAMEBUFF only).
            sw.Restart();
            for (var i = 0; i < frames; i++)
                await device.DrawTouchSlot(0, (i & 1) == 0 ? keyA : keyB, refresh: false);
            sw.Stop();
            Report("single slot 90x90  [no DRAW] ", frames, sw.Elapsed);

            // 2) Full grid drawn as N individual slot blits — exactly what the
            //    exclusive-mode redraw does (slots × 2 serial round-trips/frame).
            sw.Restart();
            for (var i = 0; i < frames; i++)
            {
                var bmp = (i & 1) == 0 ? keyA : keyB;
                for (var s = 0; s < slots; s++)
                    await device.DrawTouchSlot(s, bmp);
            }
            sw.Stop();
            Report($"full grid via {slots} slot blits", frames, sw.Elapsed);

            // 3) Whole screen in a single FRAMEBUFF, WITH and WITHOUT DRAW.
            var (w, h) = device.GetDisplaySize();
            if (w > 0 && h > 0)
            {
                using var screenA = MakeSolid(w, h, new SKColor(255, 0, 0));
                using var screenB = MakeSolid(w, h, new SKColor(0, 0, 255));

                sw.Restart();
                for (var i = 0; i < frames; i++)
                    await device.DrawScreen("center", (i & 1) == 0 ? screenA : screenB, refresh: true);
                sw.Stop();
                Report($"full screen {w}x{h}  [with DRAW]", frames, sw.Elapsed);

                sw.Restart();
                for (var i = 0; i < frames; i++)
                    await device.DrawScreen("center", (i & 1) == 0 ? screenA : screenB, refresh: false);
                sw.Stop();
                Report($"full screen {w}x{h}  [no DRAW] ", frames, sw.Elapsed);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Benchmark] aborted: {ex.Message}");
        }

        // Repaint the active page so the test pattern doesn't linger.
        try
        {
            if (config.CurrentTouchButtonPage?.TouchButtons != null)
            {
                foreach (var tb in config.CurrentTouchButtonPage.TouchButtons)
                    await device.DrawTouchButton(tb, config, true, device.Columns);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Benchmark] repaint failed: {ex.Message}");
        }

        Console.WriteLine("[Benchmark] done");
    }

    private static void Report(string label, int frames, TimeSpan elapsed)
    {
        var perFrame = elapsed.TotalMilliseconds / frames;
        var fps = frames / elapsed.TotalSeconds;
        Console.WriteLine(
            $"[Benchmark] {label}: {perFrame:F2} ms/frame, {fps:F1} fps " +
            $"({frames} frames in {elapsed.TotalMilliseconds:F0} ms)");
    }

    private static SKBitmap MakeSolid(int width, int height, SKColor color)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }
}

[Command("System.PlayVideo", "Play a video via ffmpeg to the touch display (toggle)", "Device Control",
    parameterTemplate: "({Path},{Fps},{Mode},{Slot})",
    parameterNames: ["Video path", "FPS", "Mode (full/tile/grid)", "Tile slot"],
    parameterTypes: [typeof(string), typeof(int), typeof(string), typeof(int)],
    Hidden = true)]
public class PlayVideoCommand(IDeviceService deviceService, IExclusiveModeService exclusiveMode) : IExecutableCommand
{
    // Shared so a second press stops playback. ffmpeg must be on PATH.
    private static readonly object Gate = new();
    private static CancellationTokenSource _cts;

    private enum VideoMode
    {
        Full, // one full-screen 480x270 framebuffer per frame
        Tile, // one single 90x90 slot
        Grid  // full frame split across all 90x90 slots, drawn as individual tiles
    }

    public Task Execute(string[] parameters)
    {
        lock (Gate)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
                Console.WriteLine("[Video] stopping");
                return Task.CompletedTask;
            }

            if (parameters is not { Length: > 0 } || string.IsNullOrWhiteSpace(parameters[0]))
            {
                Console.WriteLine("[Video] usage: System.PlayVideo(path,[fps])");
                return Task.CompletedTask;
            }

            var path = parameters[0];
            var fps = 30; // (1) configurable target rate
            if (parameters.Length > 1 && int.TryParse(parameters[1], out var f) && f > 0)
                fps = Math.Clamp(f, 1, 120);

            // Mode (all draw WITHOUT the trailing DRAW 0x0f — FRAMEBUFF only, like the
            // official software streaming a GIF):
            //   full → one full-screen 480x270 framebuffer per frame
            //   tile → video squeezed into a single 90x90 touch slot
            //   grid → full video spread across ALL touch slots as individual 90x90 tiles
            var modeStr = parameters.Length > 2 ? parameters[2].Trim().ToLowerInvariant() : "full";
            var mode = modeStr switch
            {
                "tile" => VideoMode.Tile,
                "grid" or "tiles" => VideoMode.Grid,
                _ => VideoMode.Full
            };
            var slot = 14; // single-tile mode: bottom-right slot — matches the captured GIF-on-button-14 test
            if (parameters.Length > 3 && int.TryParse(parameters[3], out var s) && s >= 0)
                slot = s;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _ = Task.Run(() => RunAsync(path, fps, mode, slot, cts));
            return Task.CompletedTask;
        }
    }

    // Pipes raw BGRA frames from ffmpeg and pushes each as ONE FRAMEBUFF write with
    // NO trailing DRAW (full-screen in "full" mode, a single 90x90 slot in "tile" mode).
    // Timing: ffmpeg emits constant-rate frames (no -re, so the consumer's wall
    // clock is the sole pace authority — option 3); each frame has a target
    // presentation time of frameIndex/fps. Frames that are already more than one
    // interval late are dropped (not drawn) so playback stays real-time instead of
    // drifting (option 2). The drop count is logged each second.
    private async Task RunAsync(string path, int fps, VideoMode mode, int slot, CancellationTokenSource cts)
    {
        var token = cts.Token;
        var provider = new VideoExclusiveProvider(() => { try { cts.Cancel(); } catch { /* disposed */ } });
        var entered = false;
        Process ffmpeg = null;
        try
        {
            var device = deviceService.Device;
            if (device == null) { Console.WriteLine("[Video] no device connected."); return; }

            // Own the display so background renderers (dynamic text, page redraws)
            // can't interleave their per-slot FRAMEBUFF/DRAW with the video frames —
            // that interleaving is what shows up as tearing.
            entered = exclusiveMode.TryEnter(provider);
            if (!entered) { Console.WriteLine("[Video] another exclusive mode is active — cannot start."); return; }

            int w, h;
            var cols = device.Columns;
            var rows = device.Rows;
            switch (mode)
            {
                case VideoMode.Tile:
                    // Single 90x90 touch slot.
                    w = 90;
                    h = 90;
                    break;
                case VideoMode.Grid:
                    // Scale to exactly the touch grid (cols*90 x rows*90) so each
                    // slot gets one 90x90 chunk with no leftover margin.
                    w = cols * 90;
                    h = rows * 90;
                    break;
                default: // Full
                    (w, h) = device.GetDisplaySize();
                    if (w <= 0 || h <= 0) { w = 480; h = 270; }
                    break;
            }

            var frameSize = w * h * 4;
            var frameIntervalMs = 1000.0 / fps;

            ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                // No -re: the consumer paces playback. -r {fps} gives constant-rate
                // output, so frame i corresponds to video time i/fps.
                Arguments = $"-i \"{path}\" -f rawvideo -r {fps} -pix_fmt bgra -vf scale={w}:{h} -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (ffmpeg == null) { Console.WriteLine("[Video] failed to start ffmpeg (is it on PATH?)."); return; }

            // Drain stderr continuously: ffmpeg logs progress there, and if we
            // leave it unread the pipe buffer fills and ffmpeg blocks — which
            // looks like playback freezing after a few seconds.
            _ = Task.Run(async () =>
            {
                try { _ = await ffmpeg.StandardError.ReadToEndAsync(token); }
                catch { /* killed / cancelled */ }
            }, token);

            var where = mode switch
            {
                VideoMode.Tile => $"tile slot {slot}",
                VideoMode.Grid => $"grid {cols}x{rows} tiles",
                _ => "full screen"
            };
            Console.WriteLine($"[Video] playing \"{path}\" at {w}x{h}, {fps} fps, {where}, no DRAW — press again to stop");

            var stream = ffmpeg.StandardOutput.BaseStream;
            var buffer = new byte[frameSize];
            var sw = Stopwatch.StartNew();

            long frameIndex = 0;
            var logStartMs = 0.0;
            var logDrawn = 0;
            var logDropped = 0;

            while (!token.IsCancellationRequested)
            {
                // Read exactly one frame from the pipe (keeps ffmpeg from stalling).
                var read = 0;
                while (read < frameSize)
                {
                    var r = await stream.ReadAsync(buffer.AsMemory(read, frameSize - read), token);
                    if (r <= 0) { Console.WriteLine("[Video] end of stream."); return; }
                    read += r;
                }

                var scheduledMs = frameIndex * frameIntervalMs;
                frameIndex++;
                var nowMs = sw.Elapsed.TotalMilliseconds;

                void TickLog()
                {
                    if (nowMs - logStartMs < 1000.0) return;
                    Console.WriteLine($"[Video] {logDrawn} drawn, {logDropped} dropped (target {fps} fps)");
                    logStartMs = nowMs;
                    logDrawn = 0;
                    logDropped = 0;
                }

                // (2) Drop frames that are already more than one interval late so
                // playback catches up instead of running in slow motion.
                if (nowMs - scheduledMs > frameIntervalMs)
                {
                    logDropped++;
                    TickLog();
                    continue;
                }

                // Early → wait until this frame's presentation time on the wall clock.
                await WaitUntilMs(sw, scheduledMs, token);

                using (var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque)))
                {
                    Marshal.Copy(buffer, 0, bmp.GetPixels(), frameSize);
                    // No DRAW (0x0f) refresh: write only the framebuffer, exactly like
                    // the official software does when streaming frames to the device.
                    switch (mode)
                    {
                        case VideoMode.Tile:
                            await device.DrawTouchSlot(slot, bmp, refresh: false);
                            break;
                        case VideoMode.Grid:
                            await DrawGridTiles(device, bmp, cols, rows, token);
                            break;
                        default: // Full
                            await device.DrawScreen("center", bmp, refresh: false);
                            break;
                    }
                }

                logDrawn++;
                TickLog();
            }
        }
        catch (OperationCanceledException) { /* stopped */ }
        catch (Exception ex) { Console.WriteLine($"[Video] error: {ex.Message}"); }
        finally
        {
            try { if (ffmpeg is { HasExited: false }) ffmpeg.Kill(true); } catch { /* already gone */ }
            ffmpeg?.Dispose();

            // Leaving exclusive mode makes the controller repaint the active page.
            if (entered) exclusiveMode.Exit(provider);

            lock (Gate) { if (_cts == cts) _cts = null; }
            Console.WriteLine("[Video] stopped");
        }
    }

    // Splits one full-grid frame (cols*90 x rows*90) into individual 90x90 tiles and
    // writes each as its own FRAMEBUFF (no DRAW). One reusable tile bitmap is filled
    // per slot — the per-slot DrawTouchSlot is awaited before the next overwrite, so
    // sharing the buffer is safe.
    private static async Task DrawGridTiles(LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice device,
        SKBitmap full, int cols, int rows, CancellationToken token)
    {
        using var tile = new SKBitmap(new SKImageInfo(90, 90, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(tile);
        var dst = new SKRect(0, 0, 90, 90);

        for (var s = 0; s < cols * rows; s++)
        {
            if (token.IsCancellationRequested) return;
            var col = s % cols;
            var row = s / cols;
            var src = new SKRect(col * 90, row * 90, (col * 90) + 90, (row * 90) + 90);
            canvas.DrawBitmap(full, src, dst); // opaque source fully overwrites the tile
            await device.DrawTouchSlot(s, tile, refresh: false);
        }
    }

    // Waits until the playback clock reaches targetMs (no-op if already past).
    // Coarse sleep for the bulk, short spin for the tail to avoid Task.Delay jitter.
    private static async Task WaitUntilMs(Stopwatch sw, double targetMs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var remain = targetMs - sw.Elapsed.TotalMilliseconds;
            if (remain <= 0) return;
            if (remain > 3) await Task.Delay(1, token);
            else Thread.SpinWait(200);
        }
    }

    // Minimal exclusive-mode owner: draws nothing itself (the command pushes video
    // frames directly) and never raises EntriesChanged, so the controller suppresses
    // all other touch rendering and stays out of the way. Any hardware input stops.
    private sealed class VideoExclusiveProvider(Action onStop) : LoupixDeck.PluginSdk.IExclusiveModeProvider
    {
        public string Title => "Video";
        public event EventHandler EntriesChanged { add { } remove { } }
        public void OnEnter() { }
        public void OnExit() { }
        public IReadOnlyList<LoupixDeck.PluginSdk.FolderEntry> BuildTouchEntries()
            => Array.Empty<LoupixDeck.PluginSdk.FolderEntry>();
        public void OnSimpleButtonPressed(int index) => onStop();
        public void OnTouchPressed(int index) => onStop();
        public void OnRotaryPressed(int index) => onStop();
        public void OnRotated(int index, int delta) { }
    }
}

[Command("System.ToggleWindow", "Toggle Main Window visibility", "Device Control")]
public class ToggleWindowCommand : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        // Window manipulation must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (WindowHelper.GetMainWindow() is Views.MainWindow mw)
                mw.ToggleVisibility();
        });
        return Task.CompletedTask;
    }
}
