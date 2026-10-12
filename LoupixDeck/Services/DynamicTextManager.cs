using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class DynamicTextManager : IDynamicTextManager, IDisposable
{
    private sealed class Entry
    {
        public TouchButton Button;

        /// <summary>
        /// Set instead of <see cref="Button"/> for a text layer of the CT wheel canvas bound to a
        /// dynamic-text command: the reported text goes to <see cref="TextLayer.LiveText"/> and the
        /// wheel is repainted, rather than a button layer being written.
        /// </summary>
        public TextLayer WheelLayer;

        /// <summary>Set instead of <see cref="WheelLayer"/> for a wheel layer bound to an image command.</summary>
        public PluginLayer WheelImageLayer;

        /// <summary>The canvas holding <see cref="WheelImageLayer"/>, told when its image changed.</summary>
        public TouchButton WheelCanvas;

        /// <summary>The wheel canvas's runtime key, handed to the command in place of a button's.</summary>
        public string WheelRuntimeKey;

        public RegisteredCommand Command;
        public string[] Parameters;

        /// <summary>The button's full command sequence, handed to the rendering command so it can
        /// compose from its siblings. Empty for single-command buttons.</summary>
        public IReadOnlyList<SequenceCommand> SequenceCommands;

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

    private readonly Lock _gate = new();
    private List<Entry> _active = new();
    private CancellationTokenSource _cts;
    private PeriodicTimer _timer;
    private Task _loopTask;

    private readonly LoupedeckConfig _config;

    public DynamicTextManager(
        IPageManager pageManager,
        ICommandRegistry commandRegistry,
        IServiceProvider deviceProvider,
        IDeviceRouter router,
        LoupedeckConfig config)
    {
        _pageManager = pageManager;
        _commandRegistry = commandRegistry;
        _deviceProvider = deviceProvider;
        _router = router;
        _config = config;
    }

    public void Start()
    {
        _pageManager.TouchLayoutChanged += OnTouchLayoutChanged;
        _pageManager.TouchLayoutDrawn += OnTouchLayoutDrawn;

        // Resolved from the device provider rather than injected: the controller depends on this
        // device's services, so asking for it in the constructor would be circular.
        _controller = _deviceProvider.GetService<IDeviceController>();
        if (_controller != null)
            _controller.WheelModeChanged += OnTouchLayoutChanged;

        Rescan();
    }

    private void OnTouchLayoutChanged() => Rescan();

    /// <summary>A layer edit on the wheel canvas (command added, changed or removed) rebuilds the entries.</summary>
    private void OnWheelCanvasItemChanged(object sender, EventArgs e) => Rescan();

    private IDeviceController _controller;

    /// <summary>The wheel canvas whose edits this manager follows; one at a time, like the animation manager.</summary>
    private TouchButton _subscribedCanvas;

    private void SubscribeWheelCanvas(TouchButton canvas)
    {
        if (ReferenceEquals(_subscribedCanvas, canvas))
            return;

        if (_subscribedCanvas != null)
            _subscribedCanvas.ItemChanged -= OnWheelCanvasItemChanged;
        _subscribedCanvas = canvas;
        if (canvas != null)
            canvas.ItemChanged += OnWheelCanvasItemChanged;
    }

    /// <summary>
    /// The whole layout was just painted. A rescan publishes values as soon as the layout changes,
    /// which is before that paint, so a key whose value landed first was painted over with the
    /// value missing, and the value-unchanged check in <see cref="PublishValue"/> then never
    /// repaints it (it stayed blank until pressed). Repaint every key that holds a value now.
    /// </summary>
    private void OnTouchLayoutDrawn()
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = _active;
        }

        // Wheel entries carry no button: a command that is both a text and a value command can be
        // bound to a wheel text layer, and it has no key value to repaint here.
        var buttons = snapshot.Where(e => e.Button != null && e.Command.IsValueDisplayCommand).Select(e => (e.Button, e.OwnerKey)).ToArray();
        if (buttons.Length == 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (button, ownerKey) in buttons)
            {
                if (button.DisplayValue != null && StillBound(button, ownerKey))
                    button.Refresh();
            }
        });
    }

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

                var name = CommandStringParser.GetName(button.Command);
                if (string.IsNullOrEmpty(name))
                    continue;

                var command = _commandRegistry.Get(name);
                if (command == null)
                    continue;

                var isText = command.IsDisplayCommand && command.GetText != null;
                var isImage = command.IsImageDisplayCommand && command.RenderImage != null;
                var isValue = command.IsValueDisplayCommand && command.GetDisplayValue != null;
                if (!isText && !isImage && !isValue)
                    continue;

                var parms = CommandStringParser.GetParameters(button.Command);
                var interval = command.UpdateInterval;
                if (interval < TimeSpan.FromMilliseconds(250))
                    interval = TimeSpan.FromMilliseconds(250);

                entries.Add(new Entry
                {
                    Button = button,
                    Command = command,
                    Parameters = parms,
                    SequenceCommands = CommandStringParser.BuildSequence(button.Command),
                    Interval = interval,
                    NextDueUtc = DateTime.UtcNow,
                    OwnerKey = PluginLayerKey.For(button.Command)
                });
            }
        }

        AddWheelEntries(entries);

        // Remove/demote plugin-managed layers whose owning command is no longer bound
        // (command changed/cleared, or its plugin was uninstalled) before (re)starting the loop.
        SweepOrphanLayers(page);
        ClearStaleValues(page, entries);

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

        // A command that declares its own states renders per state, and its layer lives in the
        // state's own layer stack — so a state switch needs an immediate re-render, not the next
        // poll tick (which would leave the new state blank until then).
        foreach (Entry entry in entries)
        {
            if (entry.Button != null && entry.Command.DeclaresStates)
                Subscribe(entry.Button);
        }

        lock (_gate)
        {
            // Rescans arrive from the UI thread and from the serial-read thread (wheel mode switch);
            // one that ran alongside this one may already have started a loop.
            // Stop it here, or it keeps ticking with nothing left to cancel it.
            try { _cts?.Cancel(); } catch { }
            try { _timer?.Dispose(); } catch { }
            _cts?.Dispose();

            _active = entries;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(tickInterval);
            var token = _cts.Token;
            var timer = _timer;
            _loopTask = Task.Run(() => TickLoop(timer, token), token);
        }
    }

    /// <summary>
    /// Adds an entry for every text layer of the active wheel mode's canvas that takes its text from
    /// a dynamic-text command. Only the mode showing on the glass is scanned, so switching mode,
    /// page, workspace or profile (each of which rescans) stops the polling of the canvas that went
    /// inactive. A layer that no longer resolves to a command drops its stale live text and draws
    /// its own text again.
    /// </summary>
    private void AddWheelEntries(List<Entry> entries)
    {
        TouchButton canvas = _controller is { WheelScreenSize: > 0 }
            ? _pageManager.CurrentTouchButtonPage?.Wheel?.Canvas
            : null;
        SubscribeWheelCanvas(canvas);

        if (canvas?.Layers == null)
            return;

        AddWheelImageEntries(entries, canvas);

        foreach (TextLayer layer in canvas.Layers.OfType<TextLayer>().ToArray())
        {
            if (layer.TextSource != TextSource.Command)
                continue;

            RegisteredCommand command = null;
            string name = string.IsNullOrWhiteSpace(layer.TextCommand) ? null : CommandStringParser.GetName(layer.TextCommand);
            if (!string.IsNullOrEmpty(name))
                command = _commandRegistry.Get(name);

            if (command is not { IsDisplayCommand: true, GetText: not null })
            {
                layer.LiveText = null;
                continue;
            }

            TimeSpan interval = command.UpdateInterval;
            if (interval < TimeSpan.FromMilliseconds(250))
                interval = TimeSpan.FromMilliseconds(250);

            entries.Add(new Entry
            {
                WheelLayer = layer,
                WheelRuntimeKey = canvas.RuntimeKey,
                Command = command,
                Parameters = CommandStringParser.GetParameters(layer.TextCommand),
                SequenceCommands = CommandStringParser.BuildSequence(layer.TextCommand),
                Interval = interval,
                NextDueUtc = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Adds an entry for every plugin layer of the wheel canvas the user bound to an image command.
    /// </summary>
    private void AddWheelImageEntries(List<Entry> entries, TouchButton canvas)
    {
        foreach (PluginLayer layer in canvas.Layers.OfType<PluginLayer>().ToArray())
        {
            // Plugin-owned layers are bound through their owner key, not here.
            if (layer.IsCommandOwned)
                continue;

            string name = string.IsNullOrWhiteSpace(layer.Command) ? null : CommandStringParser.GetName(layer.Command);
            RegisteredCommand command = string.IsNullOrEmpty(name) ? null : _commandRegistry.Get(name);
            if (command is not { IsImageDisplayCommand: true, RenderImage: not null })
            {
                ClearWheelImage(layer, canvas);
                continue;
            }

            TimeSpan interval = command.UpdateInterval;
            if (interval < TimeSpan.FromMilliseconds(250))
                interval = TimeSpan.FromMilliseconds(250);

            entries.Add(new Entry
            {
                WheelImageLayer = layer,
                WheelCanvas = canvas,
                WheelRuntimeKey = canvas.RuntimeKey,
                Command = command,
                Parameters = CommandStringParser.GetParameters(layer.Command),
                SequenceCommands = CommandStringParser.BuildSequence(layer.Command),
                Interval = interval,
                NextDueUtc = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Drops the last image of a wheel layer whose command was cleared or no longer draws an image,
    /// so the wheel stops showing a frozen frame, like the text path clearing its live text.
    /// </summary>
    private void ClearWheelImage(PluginLayer layer, TouchButton canvas)
    {
        if (layer.RenderedBitmap == null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            layer.SetLiveBitmap(null);
            _ = _controller?.RefreshWheelAnimationFrame();
            canvas.RaiseLiveContentChanged();
        });
    }

    /// <summary>
    /// Wheel counterpart of the image path in <see cref="RenderEntry"/>: draws the command at the
    /// wheel screen's size, stores the bitmap on the layer and repaints the wheel.
    /// </summary>
    private void RenderWheelImageEntry(Entry entry)
    {
        using var _routerScope = _router.Enter(_deviceProvider);

        int size = _controller?.WheelScreenSize ?? 0;
        if (size <= 0)
            return;

        SKBitmap bitmap = null;
        try
        {
            using var recording = new RecordedRender(size, size);
            lock (SkiaRenderGate.Sync)
            {
                var rc = new SkiaRenderCanvas(recording.Canvas, size, size);
                if (entry.Command.RenderImage(entry.Parameters, entry.SequenceCommands, null, entry.WheelRuntimeKey, rc))
                    bitmap = recording.Finish();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DynamicTextManager: image command '{entry.Command.CommandName}' threw: {ex.Message}");
            return;
        }

        if (bitmap == null)
            return;

        PluginLayer layer = entry.WheelImageLayer;
        Dispatcher.UIThread.Post(() =>
        {
            lock (_gate)
            {
                if (!_active.Contains(entry))
                {
                    bitmap.Dispose();
                    return;
                }
            }

            // Not RenderedBitmap: its change notification reaches the canvas's ItemChanged, which
            // rescans, which re-renders at once - an endless loop on the UI thread. The swap here
            // only notifies when the size changes (the first frame) and the repaint is explicit.
            layer.SetLiveBitmap(bitmap);
            _ = _controller?.RefreshWheelAnimationFrame();
            entry.WheelCanvas?.RaiseLiveContentChanged();
        });
    }

    /// <summary>
    /// Wheel counterpart of the text path in <see cref="RenderEntry"/>: reads the command's text
    /// and, when it changed, stores it on the layer and repaints the wheel on the UI thread.
    /// </summary>
    private void RenderWheelEntry(Entry entry)
    {
        using var _routerScope = _router.Enter(_deviceProvider);

        string newText;
        try
        {
            newText = entry.Command.GetText(entry.Parameters, entry.SequenceCommands, null, entry.WheelRuntimeKey) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DynamicTextManager: command '{entry.Command.CommandName}' threw: {ex.Message}");
            return;
        }

        TextLayer layer = entry.WheelLayer;
        if (string.Equals(layer.LiveText, newText, StringComparison.Ordinal))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            // The layer may have been retargeted or removed between the read and this post; a
            // rescan then rebuilt the entries and this one is no longer in the active set.
            lock (_gate)
            {
                if (!_active.Contains(entry))
                    return;
            }

            layer.LiveText = newText;
            _ = _controller?.RefreshWheelAnimationFrame();
        });
    }

    /// <summary>
    /// Buttons whose active-state changes this manager follows. Guarded by its own lock: a rescan
    /// may run off the UI thread while a state change (always dispatched to it) arrives.
    /// </summary>
    private readonly List<StatefulButton> _stateSubscriptions = [];
    private readonly Lock _subscriptionGate = new();

    private void Subscribe(StatefulButton button)
    {
        if (button == null)
            return;

        lock (_subscriptionGate)
        {
            if (_stateSubscriptions.Contains(button))
                return;

            button.ActiveStateChanged += OnActiveStateChanged;
            _stateSubscriptions.Add(button);
        }
    }

    private void UnsubscribeAll()
    {
        lock (_subscriptionGate)
        {
            foreach (StatefulButton button in _stateSubscriptions)
                button.ActiveStateChanged -= OnActiveStateChanged;

            _stateSubscriptions.Clear();
        }
    }

    private void OnActiveStateChanged(object sender, EventArgs e)
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = _active;
        }

        foreach (Entry entry in snapshot)
        {
            if (ReferenceEquals(entry.Button, sender))
                RenderEntry(entry);
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
        if (entry.WheelImageLayer != null)
        {
            RenderWheelImageEntry(entry);
            return;
        }

        if (entry.WheelLayer != null)
        {
            RenderWheelEntry(entry);
            return;
        }

        var command = entry.Command;
        var button = entry.Button;
        if (command == null || button == null)
            return;

        // Plugin display commands run plugin code (GetText/RenderImage) that may call
        // back into the host — mark this device as the ambient target so those calls
        // reach THIS device (issue #116 phase 2).
        using var _routerScope = _router.Enter(_deviceProvider);

        // Read the state at render time: the active state can change between two ticks, and the
        // command's content (and the layer it lands on) follows the state, not the entry.
        string stateName = command.DeclaresStates ? button.ActiveState?.Name : null;

        if (command.IsValueDisplayCommand && command.GetDisplayValue != null)
            PublishValue(entry);

        if (command.IsImageDisplayCommand && command.RenderImage != null)
        {
            // The plugin draws the button onto a host canvas at the device's key size; serialize
            // with all other Skia work (font/glyph caches + the layer's gated bitmap swap) so it
            // can't race the pipeline.
            // Recorded, so the on-screen device view can play the plugin's drawing back sharp at its
            // own size (#251) without asking the plugin to draw a second time.
            int keySize = _config?.EffectiveKeyCalibration.KeySize ?? DeviceGeometry.Default.KeySize;
            SKBitmap bitmap = null;
            try
            {
                using var recording = new RecordedRender(keySize, keySize);
                lock (SkiaRenderGate.Sync)
                {
                    var rc = new SkiaRenderCanvas(recording.Canvas, keySize, keySize);
                    if (command.RenderImage(entry.Parameters, entry.SequenceCommands, stateName, button.RuntimeKey, rc))
                        bitmap = recording.Finish();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DynamicTextManager: image command '{command.CommandName}' threw: {ex.Message}");
                return;
            }

            // The plugin declined (no data yet): leave the button unchanged.
            if (bitmap == null)
                return;

            var ownerKey = entry.OwnerKey;
            var name = command.CommandName;
            Dispatcher.UIThread.Post(() =>
            {
                // The button may have been cleared or rebound between the render and this post —
                // creating the layer now would leave one behind that no sweep comes back for.
                if (!StillBound(button, ownerKey))
                {
                    bitmap.Dispose();
                    return;
                }

                button.GetOrCreatePluginLayer(ownerKey, name).RenderedBitmap = bitmap; // setter retires the old bitmap under the gate
            });
            return;
        }

        if (!command.IsDisplayCommand || command.GetText == null)
            return;

        // Text path.
        string newText;
        try
        {
            newText = command.GetText(entry.Parameters, entry.SequenceCommands, stateName, button.RuntimeKey) ?? string.Empty;
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
        Dispatcher.UIThread.Post(() =>
        {
            if (!StillBound(button, key))
                return;

            button.GetOrAdoptOwnedTextLayer(key, cmdName).Text = newText;
        });
    }

    /// <summary>
    /// Reads the value of a value display command and hands it to the button, which redraws its
    /// indicator and value text layers when it changed. The value is read here, off the UI thread,
    /// like every other plugin render call.
    /// </summary>
    private static void PublishValue(Entry entry)
    {
        var button = entry.Button;
        string stateName = entry.Command.DeclaresStates ? button.ActiveState?.Name : null;
        AdjustmentValue? value = entry.Command.GetDisplayValue(entry.Parameters, entry.SequenceCommands, stateName,
            button.RuntimeKey);

        var ownerKey = entry.OwnerKey;
        Dispatcher.UIThread.Post(() =>
        {
            if (!StillBound(button, ownerKey) || Equals(button.DisplayValue, value))
                return;

            button.DisplayValue = value;
            button.Refresh();
        });
    }

    /// <summary>
    /// Drops the value a button kept from a value command it is no longer bound to, so its
    /// indicator and value text layers stop showing it.
    /// </summary>
    private static void ClearStaleValues(TouchButtonPage page, List<Entry> entries)
    {
        if (page?.TouchButtons == null)
            return;

        var stale = page.TouchButtons
            .Where(button => button?.DisplayValue != null &&
                             !entries.Any(e => ReferenceEquals(e.Button, button) && e.Command.IsValueDisplayCommand))
            .ToArray();
        if (stale.Length == 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var button in stale)
            {
                button.DisplayValue = null;
                button.Refresh();
            }
        });
    }

    /// <summary>
    /// True while <paramref name="button"/> is still bound to the command that produced
    /// <paramref name="ownerKey"/> — checked on the UI thread right before a command-owned layer
    /// is created, since the render itself ran off it.
    /// </summary>
    private static bool StillBound(TouchButton button, string ownerKey) =>
        string.Equals(PluginLayerKey.For(button?.Command), ownerKey, StringComparison.Ordinal);

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
                if (button?.States == null)
                    continue;

                var (validKey, validKind) = ResolveDisplayKey(button);

                // A bound command whose plugin is not loaded (missing, disabled, not yet installed)
                // keeps its layer: the plugin may come back, and the layer carries the user's
                // placement and visibility for it.
                var unresolvedKey = validKey == null && IsUnregistered(button.Command)
                    ? PluginLayerKey.For(button.Command)
                    : null;

                // Layers live per state, and a command that declares states owns one layer in each
                // of them — so sweep every state, not just the active one.
                foreach (var state in button.States.ToArray())
                foreach (var layer in state.Layers.ToArray())
                {
                    if (!layer.IsCommandOwned)
                        continue;
                    // Valid only when the key matches AND the layer is the kind the bound command
                    // renders now. A command that moved from text to image (or back) in a plugin
                    // update leaves a layer with the right key but the wrong kind; it falls through
                    // to the same removal/demotion rules as a layer whose command is gone.
                    if (validKey != null && string.Equals(layer.OwnerKey, validKey, StringComparison.Ordinal) &&
                        LayerMatchesKind(layer, validKind))
                        continue;
                    if (layer is PluginLayer && unresolvedKey != null &&
                        string.Equals(layer.OwnerKey, unresolvedKey, StringComparison.Ordinal))
                        continue;

                    switch (layer)
                    {
                        case PluginLayer plugin:
                            state.Layers.Remove(plugin);
                            plugin.DisposeBitmaps();
                            break;
                        // A layer the command created is removed with the command; a layer that
                        // was adopted from a pre-existing user layer is only demoted (owner cleared,
                        // text + styling kept) so the user's work is never destroyed.
                        case TextLayer text when text.OwnerCreated:
                            state.Layers.Remove(text);
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
    /// <summary>
    /// The owner key the button's currently bound command would produce, or <c>null</c> when
    /// the button is not bound to a registered text/image display command.
    /// </summary>
    /// <summary>True when the command string names a command that is not registered on this device.</summary>
    private bool IsUnregistered(string command)
    {
        var name = string.IsNullOrWhiteSpace(command) ? null : CommandStringParser.GetName(command);
        return !string.IsNullOrEmpty(name) && _commandRegistry.Get(name) == null;
    }

    /// <summary>The layer kind a display command owns on a button.</summary>
    private enum DisplayLayerKind
    {
        None,
        Text,   // IDisplayCommand → TextLayer
        Image   // IDisplayImageCommand / IAnimatedDisplayCommand → PluginLayer
    }

    private static bool LayerMatchesKind(LayerBase layer, DisplayLayerKind kind) => kind switch
    {
        DisplayLayerKind.Image => layer is PluginLayer,
        DisplayLayerKind.Text => layer is TextLayer,
        _ => false
    };

    /// <summary>
    /// The owner key the button's currently bound command would produce and the layer kind that
    /// command renders, or <c>(null, None)</c> when the button has no display-capable command
    /// bound. Image wins over text for a command that implements both, matching
    /// <see cref="RenderEntry"/>.
    /// </summary>
    private (string Key, DisplayLayerKind Kind) ResolveDisplayKey(TouchButton button)
    {
        if (button == null || string.IsNullOrWhiteSpace(button.Command))
            return (null, DisplayLayerKind.None);

        var name = CommandStringParser.GetName(button.Command);
        if (string.IsNullOrEmpty(name))
            return (null, DisplayLayerKind.None);

        var command = _commandRegistry.Get(name);
        if (command == null)
            return (null, DisplayLayerKind.None);

        var isText = command.IsDisplayCommand && command.GetText != null;
        var isImage = command.IsImageDisplayCommand && command.RenderImage != null;
        // Animated commands are driven by the button-animation engine, not this poll, but they own a
        // plugin layer too — treat them as a valid display key so the orphan sweep below keeps their
        // layer alive instead of deleting it on every rescan.
        var isAnimated = command.IsAnimatedImageCommand && command.RenderAnimatedFrame != null;
        if (!isText && !isImage && !isAnimated)
            return (null, DisplayLayerKind.None);

        var kind = isImage || isAnimated ? DisplayLayerKind.Image : DisplayLayerKind.Text;
        return (PluginLayerKey.For(button.Command), kind);
    }

    private void StopLoop()
    {
        UnsubscribeAll();

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
        _pageManager.TouchLayoutChanged -= OnTouchLayoutChanged;
        _pageManager.TouchLayoutDrawn -= OnTouchLayoutDrawn;
        if (_controller != null)
            _controller.WheelModeChanged -= OnTouchLayoutChanged;
        SubscribeWheelCanvas(null);
        StopLoop();
    }

}
