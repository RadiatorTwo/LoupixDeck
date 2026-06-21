using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services;

public interface IDynamicTextManager
{
    void Start();
    void Rescan();

    /// <summary>
    /// Forces an immediate re-render of every active dynamic-text button bound
    /// to <paramref name="commandName"/>, bypassing the next poll tick. Used by
    /// <see cref="LoupixDeck.PluginSdk.IPluginHost.RequestButtonRefresh"/> when
    /// a plugin's data arrives via push.
    /// </summary>
    void RefreshCommand(string commandName);
}

public class DynamicTextManager : IDynamicTextManager, IDisposable
{
    private sealed class Entry
    {
        public TouchButton Button;
        public RegisteredCommand Command;
        public string[] Parameters;
        public TimeSpan Interval;
        public DateTime NextDueUtc;

        /// <summary>
        /// Canonical owner key (<c>name(p1,p2,…)</c>) tying this command's content to its
        /// own layer, so an update targets exactly that layer instead of "the first match".
        /// </summary>
        public string OwnerKey;
    }

    private readonly IPageManager _pageManager;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IServiceProvider _deviceProvider;
    private readonly IDeviceRouter _router;

    private readonly object _gate = new();
    private List<Entry> _active = new();
    private CancellationTokenSource _cts;
    private PeriodicTimer _timer;
    private Task _loopTask;

    public DynamicTextManager(
        IPageManager pageManager,
        ICommandRegistry commandRegistry,
        IServiceProvider deviceProvider,
        IDeviceRouter router)
    {
        _pageManager = pageManager;
        _commandRegistry = commandRegistry;
        _deviceProvider = deviceProvider;
        _router = router;
    }

    public void Start()
    {
        _pageManager.OnTouchPageChanged += OnTouchPageChanged;
        Rescan();
    }

    private void OnTouchPageChanged(int previous, int current) => Rescan();

    public void Rescan()
    {
        StopLoop();

        var page = _pageManager.CurrentTouchButtonPage;
        var entries = new List<Entry>();

        if (page?.TouchButtons != null)
        {
            foreach (var button in page.TouchButtons)
            {
                if (button == null || string.IsNullOrWhiteSpace(button.Command))
                    continue;

                var name = ParseCommandName(button.Command);
                if (string.IsNullOrEmpty(name))
                    continue;

                var command = _commandRegistry.Get(name);
                if (command == null)
                    continue;

                var isText = command.IsDisplayCommand && command.GetText != null;
                var isImage = command.IsImageDisplayCommand && command.RenderImage != null;
                if (!isText && !isImage)
                    continue;

                var parms = ParseParameters(button.Command);
                var interval = command.UpdateInterval;
                if (interval < TimeSpan.FromMilliseconds(250))
                    interval = TimeSpan.FromMilliseconds(250);

                entries.Add(new Entry
                {
                    Button = button,
                    Command = command,
                    Parameters = parms,
                    Interval = interval,
                    NextDueUtc = DateTime.UtcNow,
                    OwnerKey = PluginLayerKey.For(button.Command)
                });
            }
        }

        // Remove/demote plugin-managed layers whose owning command is no longer bound
        // (command changed/cleared, or its plugin was uninstalled) before (re)starting the loop.
        SweepOrphanLayers(page);

        if (entries.Count == 0)
            return;

        var minInterval = entries.Min(e => e.Interval);
        // Tick faster than the smallest interval so wall-clock-aligned NextDue
        // boundaries are hit promptly (perceived smoothness for the clock).
        var tickInterval = TimeSpan.FromTicks(minInterval.Ticks / 4);
        if (tickInterval < TimeSpan.FromMilliseconds(100))
            tickInterval = TimeSpan.FromMilliseconds(100);
        if (tickInterval > minInterval)
            tickInterval = minInterval;

        // Pre-align each entry's first NextDue to the next wall-clock interval boundary
        // so e.g. a 1s clock fires exactly when the wall-clock second rolls over.
        var nowAlign = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            entry.NextDueUtc = AlignedNext(nowAlign, entry.Interval);
        }

        lock (_gate)
        {
            _active = entries;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(tickInterval);
            var token = _cts.Token;
            var timer = _timer;
            _loopTask = Task.Run(() => TickLoop(timer, token), token);
        }
    }

    public void RefreshCommand(string commandName)
    {
        if (string.IsNullOrEmpty(commandName))
            return;

        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = _active;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in snapshot)
        {
            if (!string.Equals(entry.Command?.CommandName, commandName, StringComparison.Ordinal))
                continue;

            RenderEntry(entry);

            // Re-align the next poll so we don't fire again immediately after this push.
            entry.NextDueUtc = AlignedNext(now, entry.Interval);
        }
    }

    private static DateTime AlignedNext(DateTime from, TimeSpan interval)
    {
        var ticks = interval.Ticks;
        if (ticks <= 0) return from;
        var next = ((from.Ticks / ticks) + 1) * ticks;
        return new DateTime(next, from.Kind);
    }

    private async Task TickLoop(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            // Fire once immediately so dynamic buttons populate without waiting for the
            // first aligned boundary (the very first DispatchUpdates uses an immediate
            // fallback for entries whose NextDue still lies in the future).
            DispatchUpdates(initial: true);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                DispatchUpdates(initial: false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DynamicTextManager tick loop error: {ex.Message}");
        }
    }

    private void DispatchUpdates(bool initial)
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = _active;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in snapshot)
        {
            // Initial pass: render once immediately even if the aligned boundary
            // hasn't been reached yet, so the button isn't blank for up to one interval.
            if (!initial && now < entry.NextDueUtc)
                continue;

            RenderEntry(entry);

            // Advance NextDue by exactly one interval to stay aligned to the wall clock.
            // If we fell behind by more than one interval, snap forward.
            entry.NextDueUtc += entry.Interval;
            if (entry.NextDueUtc <= now)
                entry.NextDueUtc = AlignedNext(now, entry.Interval);
        }
    }

    /// <summary>
    /// Pulls the current content for one entry and pushes it onto the entry's owner-keyed
    /// layer on the UI thread: a text command updates its <see cref="TextLayer"/>, an image
    /// command decodes the PNG and updates its <see cref="PluginLayer"/> (plus optional
    /// overlay text). Each command's content targets exactly its own layer (no first-match).
    /// </summary>
    private void RenderEntry(Entry entry)
    {
        var command = entry.Command;
        var button = entry.Button;
        if (command == null || button == null)
            return;

        // Plugin display commands run plugin code (GetText/RenderImage) that may call
        // back into the host — mark this device as the ambient target so those calls
        // reach THIS device (issue #116 phase 2).
        using var _routerScope = _router.Enter(_deviceProvider);

        if (command.IsImageDisplayCommand && command.RenderImage != null)
        {
            // The plugin draws the 90×90 button onto a host canvas; serialize with all other Skia
            // work (font/glyph caches + the layer's gated bitmap swap) so it can't race the pipeline.
            var bitmap = new SKBitmap(90, 90);
            bool drew;
            try
            {
                lock (SkiaRenderGate.Sync)
                {
                    using var canvas = new SKCanvas(bitmap);
                    var rc = new SkiaRenderCanvas(canvas, 90, 90);
                    drew = command.RenderImage(entry.Parameters, rc);
                    if (drew) canvas.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DynamicTextManager: image command '{command.CommandName}' threw: {ex.Message}");
                bitmap.Dispose();
                return;
            }

            if (!drew)
            {
                bitmap.Dispose(); // plugin declined (no data yet) → leave the button unchanged
                return;
            }

            var ownerKey = entry.OwnerKey;
            var name = command.CommandName;
            Dispatcher.UIThread.Post(() =>
                button.GetOrCreatePluginLayer(ownerKey, name).RenderedBitmap = bitmap); // setter retires the old bitmap under the gate
            return;
        }

        // Text path.
        string newText;
        try
        {
            newText = command.GetText(entry.Parameters) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DynamicTextManager: command '{command.CommandName}' threw: {ex.Message}");
            return;
        }

        // Every display command (core or plugin) targets its own owner-keyed layer so an
        // update lands on exactly that layer instead of "the first matching text layer".
        var key = entry.OwnerKey;
        var cmdName = command.CommandName;
        Dispatcher.UIThread.Post(() => button.GetOrAdoptOwnedTextLayer(key, cmdName).Text = newText);
    }

    /// <summary>
    /// Removes/demotes command-owned layers on <paramref name="page"/> whose owning command
    /// is no longer bound to their button: a <see cref="PluginLayer"/> is disposed and removed,
    /// a command-created <see cref="TextLayer"/> is removed, and a TextLayer that was adopted from
    /// a pre-existing user layer is demoted to a normal user layer (owner cleared, text + styling
    /// kept). Runs on the UI thread since it mutates layer collections bound to the editor.
    /// </summary>
    private void SweepOrphanLayers(TouchButtonPage page)
    {
        if (page?.TouchButtons == null)
            return;

        var buttons = page.TouchButtons.ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var button in buttons)
            {
                if (button?.Layers == null)
                    continue;

                var validKey = ResolveDisplayKey(button);

                foreach (var layer in button.Layers.ToArray())
                {
                    if (!layer.IsCommandOwned)
                        continue;
                    if (validKey != null && string.Equals(layer.OwnerKey, validKey, StringComparison.Ordinal))
                        continue;

                    switch (layer)
                    {
                        case PluginLayer plugin:
                            button.Layers.Remove(plugin);
                            plugin.DisposeBitmaps();
                            break;
                        // A layer the command created is removed with the command; a layer that
                        // was adopted from a pre-existing user layer is only demoted (owner cleared,
                        // text + styling kept) so the user's work is never destroyed.
                        case TextLayer text when text.OwnerCreated:
                            button.Layers.Remove(text);
                            break;
                        case TextLayer text:
                            text.OwnerKey = null;
                            text.CommandName = null;
                            break;
                    }
                }
            }
        });
    }

    /// <summary>
    /// The owner key the button's currently bound command would produce, or <c>null</c> when
    /// the button is not bound to a registered text/image display command.
    /// </summary>
    private string ResolveDisplayKey(TouchButton button)
    {
        if (button == null || string.IsNullOrWhiteSpace(button.Command))
            return null;

        var name = ParseCommandName(button.Command);
        if (string.IsNullOrEmpty(name))
            return null;

        var command = _commandRegistry.Get(name);
        if (command == null)
            return null;

        var isText = command.IsDisplayCommand && command.GetText != null;
        var isImage = command.IsImageDisplayCommand && command.RenderImage != null;
        // Animated commands are driven by the button-animation engine, not this poll, but they own a
        // plugin layer too — treat them as a valid display key so the orphan sweep below keeps their
        // layer alive instead of deleting it on every rescan.
        var isAnimated = command.IsAnimatedImageCommand && command.RenderAnimatedFrame != null;
        if (!isText && !isImage && !isAnimated)
            return null;

        return PluginLayerKey.For(button.Command);
    }

    private void StopLoop()
    {
        CancellationTokenSource cts;
        PeriodicTimer timer;
        lock (_gate)
        {
            cts = _cts;
            timer = _timer;
            _cts = null;
            _timer = null;
            _active = new List<Entry>();
        }

        try { cts?.Cancel(); } catch { }
        try { timer?.Dispose(); } catch { }
        cts?.Dispose();
    }

    public void Dispose()
    {
        _pageManager.OnTouchPageChanged -= OnTouchPageChanged;
        StopLoop();
    }

    private static string ParseCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var end = command.IndexOf('(');
        return end == -1 ? command : command.Substring(0, end);
    }

    private static string[] ParseParameters(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return Array.Empty<string>();

        var start = command.IndexOf('(');
        var end = command.IndexOf(')');
        if (start == -1 || end == -1 || end <= start)
            return Array.Empty<string>();

        var parameterString = command.Substring(start + 1, end - start - 1);
        return parameterString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
