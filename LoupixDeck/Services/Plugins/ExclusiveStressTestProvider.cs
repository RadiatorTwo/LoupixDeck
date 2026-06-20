using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Built-in exclusive-mode provider for benchmarking the redraw pipeline without a
/// real plugin. It fills all touch slots with a pulsing red background plus a frame
/// counter and raises <see cref="EntriesChanged"/> at a fixed rate, driving the same
/// path a chatty plugin would, so you can visually confirm the redraw stays clean
/// at the configured rate. Any touch or button press exits the test.
/// </summary>
public sealed class ExclusiveStressTestProvider : IExclusiveModeProvider, IDisposable
{
    private readonly int _intervalMs;
    private readonly Action _requestExit;
    private Timer _timer;
    private int _frame;

    /// <param name="hz">How often per second the provider *requests* a redraw. The
    /// controller's cap decides how many actually reach the device.</param>
    /// <param name="requestExit">Callback that releases exclusive mode for this provider.</param>
    /// <param name="renderMode">Which device push strategy the host uses for this test
    /// (full screen / grid / dirty tiles / single tile).</param>
    /// <param name="singleTileSlot">Slot drawn in <see cref="ExclusiveRenderMode.SingleTile"/> mode.</param>
    public ExclusiveStressTestProvider(int hz, Action requestExit,
        ExclusiveRenderMode renderMode = ExclusiveRenderMode.FullScreen, int singleTileSlot = 14)
    {
        var clamped = Math.Clamp(hz, 1, 1000);
        _intervalMs = Math.Max(1, (int)Math.Round(1000.0 / clamped));
        _requestExit = requestExit;
        RenderMode = renderMode;
        SingleTileSlot = singleTileSlot;
    }

    /// <inheritdoc />
    public ExclusiveRenderMode RenderMode { get; }

    /// <inheritdoc />
    public int SingleTileSlot { get; }

    public string Title => "FPS Stress Test";

    public event EventHandler EntriesChanged;

    public void OnEnter()
    {
        _timer = new Timer(_ =>
        {
            Interlocked.Increment(ref _frame);
            try { EntriesChanged?.Invoke(this, EventArgs.Empty); }
            catch { /* the controller logs its own redraw failures */ }
        }, null, 0, _intervalMs);
    }

    public void OnExit()
    {
        var t = Interlocked.Exchange(ref _timer, null);
        t?.Dispose();
    }

    public IReadOnlyList<FolderEntry> BuildTouchEntries()
    {
        var frame = Volatile.Read(ref _frame);
        // Cycle the hue wheel (skipping the yellow band) so the solid background
        // visibly animates and every frame differs — that's what makes tearing
        // observable. Small step keeps the colour change gentle.
        const int yellowGap = 40;       // skip the [40,80) hue band (yellow ≈ 60°)
        const int span = 360 - yellowGap;
        var hue = ((frame * 2) % span + 80) % 360;
        var bg = HueToColor(hue);

        var entries = new List<FolderEntry>(FolderNavigation.FolderConstants.TotalSlots);
        for (var slot = 0; slot < FolderNavigation.FolderConstants.TotalSlots; slot++)
        {
            entries.Add(new FolderEntry
            {
                SlotIndex = slot,
                Text = frame.ToString(),
                BackColor = bg,
                TextColor = PluginColor.White,
                TextSize = 20,
                Bold = true
            });
        }
        return entries;
    }

    /// <summary>Maps a hue (0–360°) at full saturation/value to an opaque color.</summary>
    private static PluginColor HueToColor(int hue)
    {
        var h = (hue % 360) / 60;
        var f = (hue % 360) / 60.0 - h;
        var x = (byte)(255 * (1 - f));
        var y = (byte)(255 * f);
        return h switch
        {
            0 => new PluginColor(255, y, 0, 255),
            1 => new PluginColor(x, 255, 0, 255),
            2 => new PluginColor(0, 255, y, 255),
            3 => new PluginColor(0, x, 255, 255),
            4 => new PluginColor(y, 0, 255, 255),
            _ => new PluginColor(255, 0, x, 255),
        };
    }

    public void OnSimpleButtonPressed(int index) => _requestExit();
    public void OnTouchPressed(int index) => _requestExit();
    public void OnRotaryPressed(int index) => _requestExit();
    public void OnRotated(int index, int delta) { }

    public void Dispose() => OnExit();
}
