using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Diagnostics;
using FolderGrid = LoupixDeck.Services.FolderNavigation.FolderGrid;
using IFolderNavigationService = LoupixDeck.Services.FolderNavigation.IFolderNavigationService;
using LoupixDeck.Services.Plugins;
using SkiaSharp;

namespace LoupixDeck.Commands;

/// <summary>
/// Measures the folder redraw path (#440). Opens a plugin-style folder whose keys carry PNG icons and
/// whose dials show live values, then raises <c>EntriesChanged</c> at a fixed rate while
/// <see cref="FolderRedrawProbe"/> prints where the time goes. In "dials" mode only the dial values
/// move, so the grid stays identical — the Voicemeeter case from the issue. "keys" changes the key
/// text as well. A rate of 0 opens nothing and only toggles the probe, to measure a real plugin folder.
/// Running the command again, or leaving the folder, ends the test.
/// </summary>
[Command("System.FolderStressTest", "Folder Redraw Stress Test (toggle)", "Device Control",
    parameterTemplate: "({Hz},{Mode},{IconSize})",
    parameterNames: ["Hz", "Mode", "IconSize"],
    parameterTypes: [typeof(int), typeof(string), typeof(int)],
    parameterDefaults: ["10", "dials", "256"],
    Hidden = true)]
public sealed class FolderStressTestCommand(IFolderNavigationService folderNav) : IExecutableCommand
{
    private static readonly Lock Gate = new();
    private static StressFolder _active;
    private static bool _probeOnly;

    public async Task Execute(string[] parameters)
    {
        lock (Gate)
        {
            if (_probeOnly)
            {
                _probeOnly = false;
                FolderRedrawProbe.Stop();
                return;
            }
        }

        if (Volatile.Read(ref _active) != null)
        {
            await folderNav.ExitAll();
            return;
        }

        int hz = parameters is { Length: > 0 } && int.TryParse(parameters[0], out int parsedHz) ? parsedHz : 10;
        string mode = parameters is { Length: > 1 } ? parameters[1]?.Trim().ToLowerInvariant() : "dials";
        int iconSize = parameters is { Length: > 2 } && int.TryParse(parameters[2], out int parsedSize) && parsedSize > 0
            ? parsedSize
            : 256;

        if (hz <= 0)
        {
            lock (Gate) _probeOnly = true;
            FolderRedrawProbe.Start();
            Console.WriteLine("[FolderStress] probe only — open a plugin folder now; run again to stop.");
            return;
        }

        StressFolder folder = new(folderNav.Grid, hz, mode == "keys", iconSize);
        Volatile.Write(ref _active, folder);
        Console.WriteLine($"[FolderStress] {hz} Hz, mode={(mode == "keys" ? "keys" : "dials")}, icons {iconSize}px");
        await folderNav.OpenFolder(new PluginFolderAdapter(folder));
    }

    private sealed class StressFolder : FolderProviderBase
    {
        private const int DialCount = 7;

        private readonly FolderGrid _grid;
        private readonly int _intervalMs;
        private readonly bool _changeKeys;
        private readonly Dictionary<int, byte[]> _icons = new();
        private readonly IReadOnlyDictionary<int, RotaryOverride> _overrides;
        private Timer _timer;
        private int _tick;

        public StressFolder(FolderGrid grid, int hz, bool changeKeys, int iconSize)
        {
            _grid = grid;
            _intervalMs = Math.Max(1, (int)Math.Round(1000.0 / Math.Clamp(hz, 1, 1000)));
            _changeKeys = changeKeys;

            for (int slot = 0; slot < grid.GridSlots; slot++)
            {
                if (slot != grid.BackSlotIndex)
                    _icons[slot] = RenderIcon(slot, iconSize);
            }

            Dictionary<int, RotaryOverride> overrides = new();
            for (int dial = 0; dial < DialCount; dial++)
            {
                int captured = dial;
                overrides[dial] = new RotaryOverride
                {
                    Label = $"Gain {dial + 1}",
                    GetValue = () => DialValue(captured)
                };
            }

            _overrides = overrides;
        }

        public override string Title => "Folder Stress Test";

        public override IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => _overrides;

        public override void OnEnter()
        {
            FolderRedrawProbe.Start();
            _timer = new Timer(_ =>
            {
                Interlocked.Increment(ref _tick);
                RaiseEntriesChanged();
            }, null, _intervalMs, _intervalMs);
        }

        public override void OnExit()
        {
            Interlocked.Exchange(ref _timer, null)?.Dispose();
            FolderRedrawProbe.Stop();
            Interlocked.CompareExchange(ref _active, null, this);
        }

        public override IReadOnlyList<FolderEntry> BuildEntries()
        {
            int tick = Volatile.Read(ref _tick);
            List<FolderEntry> entries = new(_icons.Count);
            foreach ((int slot, byte[] icon) in _icons)
            {
                entries.Add(new FolderEntry
                {
                    SlotIndex = slot,
                    Image = icon,
                    Text = _changeKeys ? $"Strip {slot + 1}\n{tick}" : $"Strip {slot + 1}",
                    TextSize = 14
                });
            }

            return entries;
        }

        private AdjustmentValue? DialValue(int dial)
        {
            double phase = (Volatile.Read(ref _tick) * 0.05) + (dial * 0.7);
            double normalized = (Math.Sin(phase) + 1) / 2;
            return new AdjustmentValue(normalized, $"{(normalized * 72) - 60:F1} dB");
        }

        private static byte[] RenderIcon(int slot, int size)
        {
            using SKBitmap bitmap = new(size, size);
            using (SKCanvas canvas = new(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using SKPaint paint = new();
                paint.IsAntialias = true;
                paint.Color = SKColor.FromHsv((slot * 37) % 360, 70, 90);
                canvas.DrawCircle(size / 2f, size / 2f, size * 0.4f, paint);
                paint.Color = SKColors.White;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = size * 0.05f;
                canvas.DrawCircle(size / 2f, size / 2f, size * 0.25f, paint);
            }

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
