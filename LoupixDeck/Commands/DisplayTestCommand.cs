using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Diagnostics;
using LoupixDeck.Services.Plugins;
using SkiaSharp;

namespace LoupixDeck.Commands;

/// <summary>
/// Draws diagnostic test patterns on the touch display so the rendered output can be
/// checked against the geometry the app assumes for the connected device — and, when it
/// does not match, so the specific mismatch (key size, slot order, panel size/offset,
/// colour channel order) can be identified rather than guessed at.
///
/// The command takes the display over through exclusive mode, exactly like the video and
/// stress-test diagnostics, so no page redraw, dynamic text or side-strip provider can
/// interleave its own frames. Pressing any key/button/knob on the device leaves the mode
/// and repaints the normal page; running the command a second time also ends it.
/// </summary>
[Command("System.DisplayTest", "Display Test Pattern (toggle)", "Device Control",
    parameterTemplate: "({Pattern},{Seconds})",
    parameterNames: ["Pattern (cycle/grid/ruler/color/edges)", "Seconds per pattern"],
    parameterTypes: [typeof(string), typeof(int)],
    parameterDefaults: ["cycle", "5"],
    Icon = "\U000F03D8",
    Description = "Check the display output against the device geometry — press any key to end")]
public sealed class DisplayTestCommand(IDeviceService deviceService, IExclusiveModeService exclusiveMode)
    : IExecutableCommand
{
    // Shared so a second invocation (from any button) stops a running test.
    private static readonly Lock Gate = new();
    private static CancellationTokenSource _cts;

    public Task Execute(string[] parameters)
    {
        lock (Gate)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
                Console.WriteLine("[DisplayTest] stopping");
                return Task.CompletedTask;
            }

            string patternArg = parameters is { Length: > 0 } ? parameters[0]?.Trim().ToLowerInvariant() : null;
            bool cycle = string.IsNullOrEmpty(patternArg) || patternArg == "cycle";
            DisplayTestPattern[] patterns = cycle
                ? [DisplayTestPattern.KeyGrid, DisplayTestPattern.Ruler, DisplayTestPattern.Color, DisplayTestPattern.Edges]
                : [ParsePattern(patternArg)];

            int seconds = 5;
            if (parameters is { Length: > 1 } && int.TryParse(parameters[1], out int parsed) && parsed > 0)
                seconds = Math.Clamp(parsed, 1, 120);

            CancellationTokenSource cts = new();
            _cts = cts;
            _ = Task.Run(() => RunAsync(patterns, seconds, cts));
            return Task.CompletedTask;
        }
    }

    private static DisplayTestPattern ParsePattern(string value) => value switch
    {
        "ruler" or "pixels" => DisplayTestPattern.Ruler,
        "color" or "colour" or "rgb" => DisplayTestPattern.Color,
        "edges" or "edge" or "panel" => DisplayTestPattern.Edges,
        _ => DisplayTestPattern.KeyGrid
    };

    private async Task RunAsync(DisplayTestPattern[] patterns, int seconds, CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        DisplayTestExclusiveProvider provider = new(() =>
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* already stopped */ }
        });
        bool entered = false;

        try
        {
            LoupedeckDevice.Device.LoupedeckDevice device = deviceService.Device;
            if (device == null)
            {
                Console.WriteLine("[DisplayTest] no device connected.");
                return;
            }

            entered = exclusiveMode.TryEnter(provider);
            if (!entered)
            {
                Console.WriteLine("[DisplayTest] another exclusive provider owns the display — cannot start.");
                return;
            }

            LogGeometry(device);

            int index = 0;
            while (!token.IsCancellationRequested)
            {
                DisplayTestPattern pattern = patterns[index % patterns.Length];
                index++;

                LogExpectation(pattern);
                await DrawPattern(device, pattern);

                try { await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException) { /* stopped */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[DisplayTest] error: {ex.Message}");
        }
        finally
        {
            // Leaving exclusive mode makes the controller repaint the active page.
            if (entered) exclusiveMode.Exit(provider);

            lock (Gate)
            {
                if (_cts == cts) _cts = null;
            }

            cts.Dispose();
            Console.WriteLine("[DisplayTest] stopped");
        }
    }

    /// <summary>
    /// Pushes one pattern. Tile patterns go through <c>DrawTouchSlotsAtomic</c> — the very
    /// path the page renderer uses — so what reaches the panel carries the app's real
    /// per-slot offsets. The full-panel pattern is written straight to the "center"
    /// framebuffer instead, which is the only way to see the area outside the key grid.
    /// </summary>
    private static async Task DrawPattern(LoupedeckDevice.Device.LoupedeckDevice device, DisplayTestPattern pattern)
    {
        int keySize = device.KeySize;
        int columns = device.Columns;
        int rows = device.Rows;

        if (!DisplayTestPatternRenderer.IsTilePattern(pattern))
        {
            (int width, int height) = device.GetDisplaySize();
            if (width <= 0 || height <= 0)
            {
                width = device.Geometry.PanelWidth;
                height = device.Geometry.PanelHeight;
            }

            // The CT's "center" is a dedicated grid-only buffer starting at 0; every other
            // device writes the grid into a unified panel buffer at its own origin.
            int gridOriginX = device.GridOriginX;

            using SKBitmap panel = DisplayTestPatternRenderer.RenderPanel(width, height, gridOriginX, keySize,
                columns, rows, device.Type);
            await device.DrawScreen("center", panel);
            return;
        }

        int slots = columns * rows;
        List<SKBitmap> tiles = new(slots);
        try
        {
            for (int slot = 0; slot < slots; slot++)
                tiles.Add(DisplayTestPatternRenderer.RenderTile(pattern, slot, columns, keySize));

            await device.DrawTouchSlotsAtomic(tiles);
        }
        finally
        {
            foreach (SKBitmap tile in tiles) tile.Dispose();
        }
    }

    /// <summary>
    /// Prints the numbers the patterns are drawn from. Comparing these against the panel
    /// is the whole point: if the test image is wrong, it is wrong because one of these
    /// values does not describe the hardware.
    /// </summary>
    private static void LogGeometry(LoupedeckDevice.Device.LoupedeckDevice device)
    {
        (int width, int height) = device.GetDisplaySize();
        Registry.DeviceGeometry geometry = device.Geometry;

        Console.WriteLine($"[DisplayTest] device      : {device.Type} (pid {device.ProductId})");
        Console.WriteLine($"[DisplayTest] key size    : {geometry.KeySize}px");
        Console.WriteLine($"[DisplayTest] grid        : {device.Columns} x {device.Rows} " +
                          $"= {device.Columns * geometry.KeySize} x {device.Rows * geometry.KeySize}px, " +
                          $"origin x={device.GridOriginX}");
        Console.WriteLine($"[DisplayTest] panel       : {geometry.PanelWidth} x {geometry.PanelHeight}px");
        Console.WriteLine($"[DisplayTest] framebuffer : {width} x {height}px (\"center\")");
        Console.WriteLine($"[DisplayTest] touch slots : {device.TouchButtonCount}, " +
                          $"physical keys: {geometry.PhysicalKeys}, strips: {geometry.StripWidth}px");
        Console.WriteLine("[DisplayTest] press any key/button on the device to end the test.");
    }

    private static void LogExpectation(DisplayTestPattern pattern)
    {
        string expectation = pattern switch
        {
            DisplayTestPattern.KeyGrid =>
                "every key shows a white frame touching all four edges, four coloured corner blocks " +
                "(TL magenta, TR cyan, BL yellow, BR green) and its slot number in reading order. " +
                "Missing frame/corners => the panel shows less than one key tile; wrong numbering => " +
                "the slot-to-position mapping is off.",
            DisplayTestPattern.Ruler =>
                "read the highest white label (counted from the top/left) and the highest red label " +
                "(counted from the bottom/right) still visible on a key. Their sum plus the hidden ticks " +
                "is the real visible key size — if it is below the configured key size, the tile is cropped " +
                "on the side whose labels are missing.",
            DisplayTestPattern.Color =>
                "each quadrant must match its letter: R red, G green, B blue, W white. Red and blue " +
                "swapped => the pixel byte order on the wire is wrong. The bottom strip is a grey ramp; " +
                "heavy banding points at the colour-depth conversion.",
            _ =>
                "a white 1px frame must run along the very edge of the panel with a red frame 4px inside " +
                "it, coloured brackets in all four corners and a centred crosshair. A cut-off frame => the " +
                "panel is smaller than the framebuffer; an off-centre crosshair => the framebuffer is offset. " +
                "The blue lines are where the app believes the key boundaries are."
        };

        Console.WriteLine($"[DisplayTest] {pattern}: {expectation}");
    }

    /// <summary>
    /// Minimal exclusive-mode owner: it draws nothing itself (the command pushes the frames
    /// directly), so the controller suppresses every other renderer and stays out of the way.
    /// Any hardware input ends the test.
    /// </summary>
    private sealed class DisplayTestExclusiveProvider(Action onStop) : IExclusiveModeProvider
    {
        public string Title => "Display Test";

        public event EventHandler EntriesChanged
        {
            add { }
            remove { }
        }

        public void OnEnter() { }
        public void OnExit() { }
        public IReadOnlyList<FolderEntry> BuildTouchEntries() => [];
        public void OnSimpleButtonPressed(int index) => onStop();
        public void OnTouchPressed(int slotIndex) => onStop();
        public void OnRotaryPressed(int index) => onStop();
        public void OnRotated(int index, int delta) { }
    }
}
