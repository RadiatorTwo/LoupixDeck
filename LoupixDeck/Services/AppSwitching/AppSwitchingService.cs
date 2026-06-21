using System.Diagnostics;
using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services.ActiveWindow;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.AppSwitching;

/// <summary>
/// Maps the foreground window to a deck page. Runs entirely on the UI thread: the
/// monitor event is marshalled in, debounced ~200ms (so a burst of Alt-Tabs only
/// evaluates the window that actually sticks), then matched against the rule list.
/// Switching is skipped while another owner holds the screen (device off / folder /
/// exclusive mode) — the same guard trio the controller uses for repaints.
/// </summary>
public sealed class AppSwitchingService : IAppSwitchingService
{
    private const int DebounceMs = 200;

    private readonly IActiveWindowMonitor _monitor;
    private readonly LoupedeckConfig _config;
    private readonly IPageManager _pageManager;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFolderNavigationService _folderNav;
    private readonly IDeviceController _deviceController;

    private DispatcherTimer _debounceTimer;
    private ActiveWindowInfo _pending;
    private bool _started;

    public AppSwitchingService(
        IActiveWindowMonitor monitor,
        LoupedeckConfig config,
        IPageManager pageManager,
        IExclusiveModeService exclusiveMode,
        IFolderNavigationService folderNav,
        IDeviceController deviceController)
    {
        _monitor = monitor;
        _config = config;
        _pageManager = pageManager;
        _exclusiveMode = exclusiveMode;
        _folderNav = folderNav;
        _deviceController = deviceController;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounceTimer.Tick += OnDebounceTick;

        _monitor.ActiveWindowChanged += OnActiveWindowChanged;
        _monitor.StartMonitoring();
    }

    private void OnActiveWindowChanged(object sender, ActiveWindowInfo info)
    {
        // The Windows hook fires on the UI thread already; the Linux monitor fires
        // on a background reader thread. Marshal unconditionally to be safe.
        Dispatcher.UIThread.Post(() =>
        {
            _pending = info;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void OnDebounceTick(object sender, EventArgs e)
    {
        _debounceTimer.Stop();
        var info = _pending;
        if (info == null) return;
        _ = Evaluate(info);
    }

    private async Task Evaluate(ActiveWindowInfo info)
    {
        try
        {
            if (!_config.AppSwitchingEnabled) return;

            // Skip while something else owns the screen — not re-evaluated on exit
            // (documented limitation; re-eval would be a later phase).
            if (_exclusiveMode.IsActive || _folderNav.IsActive || _deviceController.IsDeviceOff)
                return;

            // Startup race: pages may not be loaded yet.
            if (_pageManager.TouchButtonPages.Count == 0) return;

            var match = Match(info);

            int? touchIndex;
            int? rotaryIndex = null;
            if (match != null)
            {
                touchIndex = match.TouchPageIndex;
                rotaryIndex = match.RotaryPageIndex;
            }
            else
            {
                // No rule matched — use the fallback page if configured, else do nothing.
                touchIndex = _config.AppSwitchingFallbackTouchPageIndex;
            }

            if (touchIndex is { } ti && ti >= 0 && ti < _pageManager.TouchButtonPages.Count)
                await _pageManager.ApplyTouchPage(ti);

            // ApplyTouchPage/ApplyRotaryPage are no-ops when the index already matches,
            // so a re-focus of the same app does not flicker the deck.
            if (rotaryIndex is { } ri && ri >= 0 && ri < _pageManager.RotaryButtonPages.Count)
                _pageManager.ApplyRotaryPage(ri);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSwitch] evaluate failed: {ex.Message}");
        }
    }

    private AppPageBinding Match(ActiveWindowInfo info)
    {
        var process = Normalize(info.ProcessName);
        if (string.IsNullOrEmpty(process)) return null;
        var title = info.Title ?? string.Empty;

        // First match wins — rule order is priority.
        foreach (var rule in _config.AppPageBindings)
        {
            var ruleProcess = Normalize(rule.ProcessName);
            if (string.IsNullOrEmpty(ruleProcess)) continue;
            if (!string.Equals(ruleProcess, process, StringComparison.OrdinalIgnoreCase)) continue;

            if (!string.IsNullOrEmpty(rule.TitleContains) &&
                title.IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            return rule;
        }

        return null;
    }

    /// <summary>Strips a trailing ".exe" so Windows and Linux rules are portable.</summary>
    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
}
