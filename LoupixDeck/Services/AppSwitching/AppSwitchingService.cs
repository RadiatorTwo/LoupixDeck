using System.Diagnostics;
using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services.ActiveWindow;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.AppSwitching;

/// <summary>
/// Context engine (issue #132): maps the foreground window — and process starts — to profiles and
/// workspaces. On a foreground change it picks the highest-priority matching <see cref="ContextRule"/>
/// and activates its profile/workspace (optionally jumping to a page); when no rule matches it
/// restores the previously active profile or a configured fallback. A manual profile/workspace
/// selection pins the choice until the foreground app changes. Legacy <see cref="AppPageBinding"/>s
/// are folded into rules at start so pre-#132 configs behave identically.
///
/// Runs on the UI thread: the monitor event is marshalled in and debounced ~200ms (so a burst of
/// Alt-Tabs only evaluates the window that sticks). Switching is skipped while another owner holds
/// the screen (device off / folder / exclusive mode).
/// </summary>
public sealed class AppSwitchingService : IAppSwitchingService
{
    private const int DebounceMs = 200;
    private const int ProcessPollMs = 3000;

    private readonly IActiveWindowMonitor _monitor;
    private readonly LoupedeckConfig _config;
    private readonly IPageManager _pageManager;
    private readonly IExclusiveModeService _exclusiveMode;
    private readonly IFolderNavigationService _folderNav;
    private readonly IDeviceController _deviceController;
    private readonly IWorkspaceActivationService _activation;

    private DispatcherTimer _debounceTimer;
    private DispatcherTimer _processTimer;
    private ActiveWindowInfo _pending;
    private bool _started;

    // Manual-override pin: a user-initiated activation suspends auto-switching until the foreground
    // app changes away from the one that was active when the manual choice was made.
    private bool _manualOverride;
    private string _manualOverrideProcess;
    private string _lastForegroundProcess = string.Empty;

    // Restore-on-exit: the profile that was active before a rule first took over, and whether we are
    // currently inside a rule-matched app.
    private Guid? _previousProfileId;
    private bool _inMatchedApp;

    // Process-start detection: the set of rule-relevant process names seen running at the last poll.
    private HashSet<string> _runningProcesses = new(StringComparer.OrdinalIgnoreCase);

    public AppSwitchingService(
        IActiveWindowMonitor monitor,
        LoupedeckConfig config,
        IPageManager pageManager,
        IExclusiveModeService exclusiveMode,
        IFolderNavigationService folderNav,
        IDeviceController deviceController,
        IWorkspaceActivationService activation)
    {
        _monitor = monitor;
        _config = config;
        _pageManager = pageManager;
        _exclusiveMode = exclusiveMode;
        _folderNav = folderNav;
        _deviceController = deviceController;
        _activation = activation;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        FoldInLegacyBindings();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounceTimer.Tick += OnDebounceTick;

        // Seed the running set so already-running apps do not count as "just started" at launch.
        _runningProcesses = SnapshotRelevantProcesses();
        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProcessPollMs) };
        _processTimer.Tick += OnProcessPollTick;
        _processTimer.Start();

        // A manual (command/button/UI) activation pins the current app until the foreground changes.
        _activation.ManualActivation += OnManualActivation;

        _monitor.ActiveWindowChanged += OnActiveWindowChanged;
        _monitor.StartMonitoring();
    }

    /// <summary>
    /// Projects legacy <see cref="AppPageBinding"/>s into <see cref="ContextRule"/>s once, when no
    /// rules exist yet, so a pre-#132 config keeps switching. Each binding targets the active
    /// profile's home workspace and carries its old page indices; list order is preserved as
    /// descending priority (earlier binding wins, matching the old first-match-wins semantics).
    /// </summary>
    private void FoldInLegacyBindings()
    {
        if (_config.ContextRules.Count > 0 || _config.AppPageBindings.Count == 0)
            return;

        var profileId = _config.ActiveProfile?.Id;
        var homeId = _config.ActiveProfile?.HomeWorkspace?.Id;

        var priority = _config.AppPageBindings.Count;
        foreach (var binding in _config.AppPageBindings)
        {
            _config.ContextRules.Add(new ContextRule
            {
                ProcessName = binding.ProcessName,
                TitleContains = binding.TitleContains,
                ActivateProfileId = profileId,
                ActivateWorkspaceId = homeId,
                TouchPageIndex = binding.TouchPageIndex,
                RotaryPageIndex = binding.RotaryPageIndex,
                Priority = priority--
            });
        }
    }

    private void OnManualActivation()
    {
        _manualOverride = true;
        _manualOverrideProcess = _lastForegroundProcess;
    }

    private void OnActiveWindowChanged(object sender, ActiveWindowInfo info)
    {
        // The Windows hook fires on the UI thread already; the Linux monitor fires on a background
        // reader thread. Marshal unconditionally to be safe.
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

            // Skip while something else owns the screen (device off / folder / exclusive mode).
            if (_exclusiveMode.IsActive || _folderNav.IsActive || _deviceController.IsDeviceOff)
                return;

            // Startup race: pages may not be loaded yet.
            if (_pageManager.TouchButtonPages.Count == 0) return;

            var process = ContextRuleMatcher.Normalize(info.ProcessName);

            // Manual override holds until the foreground app changes away from the pinned one.
            if (_manualOverride)
            {
                if (string.Equals(process, _manualOverrideProcess, StringComparison.OrdinalIgnoreCase))
                    return;
                _manualOverride = false;
            }

            _lastForegroundProcess = process;

            var match = ContextRuleMatcher.MatchBest(_config.ContextRules, process, info.Title ?? string.Empty);
            if (match != null)
            {
                // Remember what to restore to the first time a rule takes over.
                if (!_inMatchedApp)
                {
                    _previousProfileId = _config.ActiveProfileId;
                    _inMatchedApp = true;
                }

                await ApplyRule(match);
            }
            else
            {
                await ApplyNoMatch();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSwitch] evaluate failed: {ex.Message}");
        }
    }

    /// <summary>Restores the profile to switch back to when leaving all rule-matched apps, then
    /// applies the legacy no-match fallback page (so migrated single-profile configs still jump to
    /// their configured fallback page).</summary>
    private async Task ApplyNoMatch()
    {
        var restoreId = _config.FallbackProfileId ?? (_inMatchedApp ? _previousProfileId : null);
        _inMatchedApp = false;

        if (restoreId is { } rid && rid != _config.ActiveProfileId)
            await _activation.ActivateProfile(rid, manual: false);

        if (_config.AppSwitchingFallbackTouchPageIndex is { } ti &&
            ti >= 0 && ti < _pageManager.TouchButtonPages.Count)
        {
            await _pageManager.ApplyTouchPage(ti);
        }
    }

    /// <summary>Applies a rule's actions: activate profile (opens its home), then workspace, then
    /// optional touch/rotary page inside the resulting workspace.</summary>
    private async Task ApplyRule(ContextRule rule)
    {
        if (rule.ActivateProfileId is { } pid && pid != _config.ActiveProfileId)
            await _activation.ActivateProfile(pid, manual: false);

        if (rule.ActivateWorkspaceId is { } wid && wid != _config.ActiveWorkspaceId)
            await _activation.ActivateWorkspace(wid, manual: false);

        // ApplyTouchPage/ApplyRotaryPage are no-ops when the index already matches, so re-focusing
        // the same app does not flicker the deck.
        if (rule.TouchPageIndex is { } ti && ti >= 0 && ti < _pageManager.TouchButtonPages.Count)
            await _pageManager.ApplyTouchPage(ti);

        if (rule.RotaryPageIndex is { } ri && ri >= 0 && ri < _pageManager.RotaryButtonPages.Count)
            _pageManager.ApplyRotaryPage(ri);
    }

    // ── Process-start detection ──────────────────────────────────────────────

    private void OnProcessPollTick(object sender, EventArgs e)
    {
        try
        {
            if (!_config.AppSwitchingEnabled || _manualOverride) return;
            if (_exclusiveMode.IsActive || _folderNav.IsActive || _deviceController.IsDeviceOff) return;

            var nowRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ContextRule started = null;

            foreach (var rule in _config.ContextRules)
            {
                if (!rule.ActivateOnProcessStart) continue;
                var name = ContextRuleMatcher.Normalize(rule.ProcessName);
                if (string.IsNullOrEmpty(name)) continue;
                if (!IsProcessRunning(name)) continue;

                nowRunning.Add(name);

                // Newly appeared since the last poll → treat as "just started".
                if (!_runningProcesses.Contains(name) && (started == null || rule.Priority > started.Priority))
                    started = rule;
            }

            _runningProcesses = nowRunning;

            if (started != null)
                _ = ApplyRule(started);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSwitch] process poll failed: {ex.Message}");
        }
    }

    private HashSet<string> SnapshotRelevantProcesses()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in _config.ContextRules)
        {
            if (!rule.ActivateOnProcessStart) continue;
            var name = ContextRuleMatcher.Normalize(rule.ProcessName);
            if (!string.IsNullOrEmpty(name) && IsProcessRunning(name))
                running.Add(name);
        }

        return running;
    }

    /// <summary>True when a process with the bare name is running. Mirrors the macro
    /// "process running" test (<c>Process.GetProcessesByName</c> matches the bare name on both OSes).</summary>
    private static bool IsProcessRunning(string normalizedName)
    {
        try
        {
            return Process.GetProcessesByName(normalizedName).Length > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSwitch] process lookup '{normalizedName}' failed: {ex.Message}");
            return false;
        }
    }
}
