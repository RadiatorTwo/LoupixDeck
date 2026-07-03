using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Screensaver;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="IButtonAnimationManager"/>
public sealed class ButtonAnimationManager : IButtonAnimationManager, IDisposable
{
    // Tick rate contributed by an animated image entry. Capped low on purpose: re-rendering a 90×90
    // button takes the global Skia gate, and the source dirty-checks frames anyway, so a higher rate
    // would just add contention without visible benefit. Plugins request their own rate.
    private const int ImageFps = 15;

    private readonly IPageManager _pageManager;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IAnimationScheduler _scheduler;
    private readonly IAnimatedImageCache _cache;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFolderNavigationService _folderNav;
    private readonly IScreensaverManager _screensaver;

    private readonly ButtonAnimationSource _source;

    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;
    private volatile bool _screensaverActive;

    public ButtonAnimationManager(
        IPageManager pageManager,
        ICommandRegistry commandRegistry,
        IAnimationScheduler scheduler,
        IAnimatedImageCache cache,
        IExclusiveModeService exclusiveMode,
        IFolderNavigationService folderNav,
        IScreensaverManager screensaver,
        IDeviceService deviceService,
        LoupedeckConfig config,
        IDeviceRouter router,
        IServiceProvider deviceProvider)
    {
        _pageManager = pageManager;
        _commandRegistry = commandRegistry;
        _scheduler = scheduler;
        _cache = cache;
        _exclusiveMode = exclusiveMode;
        _folderNav = folderNav;
        _screensaver = screensaver;

        _source = new ButtonAnimationSource(deviceService, config, router, deviceProvider);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _pageManager.OnTouchPageChanged += OnTouchPageChanged;
        _screensaver.Started += OnScreensaverStarted;
        _screensaver.Stopped += OnScreensaverStopped;
        _exclusiveMode.StateChanged += OnTakeoverStateChanged;
        _folderNav.StateChanged += OnTakeoverStateChanged;

        _scheduler.Register(_source);
        Rescan();
    }

    private void OnTouchPageChanged(int previous, int current) => Rescan();

    public void Rescan()
    {
        if (_disposed) return;

        var page = _pageManager.CurrentTouchButtonPage;

        // Enumerate the page and create plugin layers on the UI thread (layer collections are
        // editor-bound), then hand the heavy decode off to a background task.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            var specs = BuildSpecs(page, out var referenced);

            _ = Task.Run(() =>
            {
                try
                {
                    var entries = Materialize(specs);
                    _cache.Trim(referenced);
                    _source.SetEntries(entries);
                    UpdateEnabled();
                    if (_source.IsActive)
                        _scheduler.RequestFrame(_source);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ButtonAnimationManager: rescan failed: {ex.Message}");
                }
            });
        });
    }

    /// <summary>UI-thread pass: reads layers/bindings and creates owner-keyed plugin layers.</summary>
    private List<Spec> BuildSpecs(TouchButtonPage page, out HashSet<string> referenced)
    {
        referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new List<Spec>();

        if (page?.TouchButtons == null)
            return specs;

        foreach (var button in page.TouchButtons)
        {
            if (button == null) continue;

            // Animated image layers.
            if (button.Layers != null)
            {
                foreach (var layer in button.Layers)
                {
                    if (layer is ImageLayer { IsAnimated: true } img &&
                        !string.IsNullOrWhiteSpace(img.AnimatedAssetPath))
                    {
                        specs.Add(new Spec { Button = button, ImageLayer = img, AnimPath = img.AnimatedAssetPath });
                        referenced.Add(img.AnimatedAssetPath);
                    }
                }
            }

            // Animated plugin command bound to the button.
            if (!string.IsNullOrWhiteSpace(button.Command))
            {
                var name = PluginLayerKey.ParseCommandName(button.Command);
                var command = string.IsNullOrEmpty(name) ? null : _commandRegistry.Get(name);
                if (command is { IsAnimatedImageCommand: true, RenderAnimatedFrame: not null })
                {
                    var ownerKey = PluginLayerKey.For(button.Command);
                    var layer = button.GetOrCreatePluginLayer(ownerKey, command.CommandName);
                    specs.Add(new Spec
                    {
                        Button = button,
                        Command = command,
                        Parameters = PluginLayerKey.ParseParameters(button.Command),
                        SequenceCommands = CommandStringParser.BuildSequence(button.Command),
                        OwnerKey = ownerKey,
                        PluginLayer = layer,
                        DesiredFps = command.AnimatedTargetFps > 0 ? command.AnimatedTargetFps : 0
                    });
                }
            }
        }

        return specs;
    }

    /// <summary>Background pass: decodes animated images (cached) and builds the source entries.</summary>
    private List<ButtonAnimationSource.Entry> Materialize(List<Spec> specs)
    {
        var entries = new List<ButtonAnimationSource.Entry>(specs.Count);

        foreach (var spec in specs)
        {
            if (spec.ImageLayer != null)
            {
                var anim = _cache.Get(spec.AnimPath);
                if (anim == null) continue;

                // Seed the first frame so a controller page redraw shows content immediately,
                // before the source's first tick (no blank flash). Field swap, no notify.
                if (spec.ImageLayer.CachedImage == null && anim.Frames.Length > 0)
                    spec.ImageLayer.SetAnimationFrame(anim.Frames[0]);

                entries.Add(new ButtonAnimationSource.ImageEntry
                {
                    Button = spec.Button,
                    Layer = spec.ImageLayer,
                    Anim = anim,
                    DesiredFps = ImageFps
                });
            }
            else if (spec.Command != null && spec.PluginLayer != null)
            {
                entries.Add(new ButtonAnimationSource.PluginEntry
                {
                    Button = spec.Button,
                    Command = spec.Command,
                    Parameters = spec.Parameters,
                    SequenceCommands = spec.SequenceCommands,
                    OwnerKey = spec.OwnerKey,
                    Layer = spec.PluginLayer,
                    DesiredFps = spec.DesiredFps
                });
            }
        }

        return entries;
    }

    // ── takeover / pause coordination ──────────────────────────────────────────

    private void OnScreensaverStarted()
    {
        _screensaverActive = true;
        _source.SetEnabled(false);
    }

    private void OnScreensaverStopped()
    {
        _screensaverActive = false;
        UpdateEnabled();
        if (_source.IsActive)
            _scheduler.RequestFrame(_source);
    }

    private void OnTakeoverStateChanged()
    {
        UpdateEnabled();
        if (_source.IsActive)
            _scheduler.RequestFrame(_source);
    }

    /// <summary>Enabled only when no other feature owns the display (mirrors the controller's veto).</summary>
    private void UpdateEnabled()
    {
        var enabled = !_screensaverActive && !_exclusiveMode.IsActive && !_folderNav.IsActive;
        _source.SetEnabled(enabled);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _pageManager.OnTouchPageChanged -= OnTouchPageChanged;
        _screensaver.Started -= OnScreensaverStarted;
        _screensaver.Stopped -= OnScreensaverStopped;
        _exclusiveMode.StateChanged -= OnTakeoverStateChanged;
        _folderNav.StateChanged -= OnTakeoverStateChanged;

        try { _scheduler.Unregister(_source); } catch { /* best effort */ }
        try { _source.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>Intermediate captured on the UI thread before the background decode pass.</summary>
    private sealed class Spec
    {
        public TouchButton Button { get; init; }

        // Image animation
        public ImageLayer ImageLayer { get; init; }
        public string AnimPath { get; init; }

        // Plugin animation
        public RegisteredCommand Command { get; init; }
        public string[] Parameters { get; init; }
        public IReadOnlyList<SequenceCommand> SequenceCommands { get; init; }
        public string OwnerKey { get; init; }
        public PluginLayer PluginLayer { get; init; }
        public int DesiredFps { get; init; }
    }
}
