using System.ComponentModel;
using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Services.Animation;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Lets the companions of a group with background follow on show the background their master shows
/// (issue #665). Nothing is copied: a following companion's <see cref="LoupedeckConfig.WallpaperMaster"/>
/// points at the running master's config and the renderer draws the master's wallpaper from there, so
/// the companion's own backgrounds stay as they are and come back once the option is off, the group
/// is paused or the master goes away. This service keeps that link current and repaints the companions
/// whenever the master's background changes. Root-level singleton; all work runs on the UI thread.
/// </summary>
public sealed class CompanionBackgroundFollowService
{
    private static readonly TimeSpan RepaintDebounce = TimeSpan.FromMilliseconds(100);

    private readonly ICompanionCoordinator _coordinator;
    private readonly IDeviceHostRegistry _registry;

    // Each running host's hooks, so a removed host is unhooked exactly once. Every host is watched,
    // since any of them may become a master later.
    private readonly Dictionary<DeviceHost, HostWatch> _watches = new();

    // Masters whose background changed since the last debounced repaint.
    private readonly HashSet<DeviceHost> _changedMasters = [];

    private DispatcherTimer _repaintTimer;

    public CompanionBackgroundFollowService(ICompanionCoordinator coordinator, IDeviceHostRegistry registry)
    {
        _coordinator = coordinator;
        _registry = registry;

        foreach (DeviceHost host in _registry.Hosts)
            OnHostAdded(host);

        _registry.HostAdded += OnHostAdded;
        _registry.HostRemoved += OnHostRemoved;
        _coordinator.GroupsChanged += OnGroupsChanged;
        _coordinator.DeviceReady += OnDeviceReady;
    }

    // ── Hosts ───────────────────────────────────────────────────────────────

    private void OnHostAdded(DeviceHost host) => OnUiThread(() =>
    {
        if (_watches.ContainsKey(host)) return;

        HostWatch watch = new(host, () => OnMasterBackgroundChanged(host));
        _watches[host] = watch;
        watch.Attach();
        UpdateLinks();
    });

    private void OnHostRemoved(DeviceHost host) => OnUiThread(() =>
    {
        if (_watches.Remove(host, out HostWatch watch))
            watch.Detach();
        _changedMasters.Remove(host);
        UpdateLinks();
    });

    private void OnGroupsChanged() => OnUiThread(UpdateLinks);

    private void OnDeviceReady(DeviceHost host) => OnUiThread(UpdateLinks);

    // ── Links ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Points every running companion at the master whose backgrounds it shows, or back at its own,
    /// and repaints the ones that changed.
    /// </summary>
    private void UpdateLinks()
    {
        foreach (DeviceHost host in _registry.Hosts)
        {
            LoupedeckConfig config = host.Controller.Config;
            if (config == null) continue;

            LoupedeckConfig master = FollowedMasterConfig(host.Device.ScopeKey);
            if (ReferenceEquals(config.WallpaperMaster, master)) continue;

            config.WallpaperMaster = master;
            Console.WriteLine(master == null
                ? $"[Companions] '{host.Device.ScopeKey}' shows its own backgrounds again."
                : $"[Companions] '{host.Device.ScopeKey}' shows its master's backgrounds.");
            _ = RepaintAsync(host);
        }
    }

    /// <summary>The config of the running master whose backgrounds the device shows, or null when it
    /// shows its own: it is no companion, its group has background follow off or is paused, or its
    /// master is not running.</summary>
    private LoupedeckConfig FollowedMasterConfig(string deviceKey)
    {
        string masterKey = _coordinator.GetMasterKey(deviceKey);
        if (masterKey == null || !_coordinator.GetBackgroundFollow(masterKey)) return null;

        return _coordinator.ResolveHost(masterKey)?.Controller.Config;
    }

    // ── Repaint ─────────────────────────────────────────────────────────────

    /// <summary>A master showed another page or folder, or its wallpaper was edited: repaint the
    /// companions showing its backgrounds once the burst of notifications is over.</summary>
    private void OnMasterBackgroundChanged(DeviceHost master) => OnUiThread(() =>
    {
        if (!_coordinator.GetBackgroundFollow(master.Device.ScopeKey)) return;

        _changedMasters.Add(master);
        _repaintTimer ??= CreateRepaintTimer();
        _repaintTimer.Stop();
        _repaintTimer.Start();
    });

    private DispatcherTimer CreateRepaintTimer()
    {
        DispatcherTimer timer = new() { Interval = RepaintDebounce };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            List<LoupedeckConfig> masters = [.. _changedMasters.Select(h => h.Controller.Config)];
            _changedMasters.Clear();

            foreach (DeviceHost host in _registry.Hosts)
            {
                LoupedeckConfig followed = host.Controller.Config?.WallpaperMaster;
                if (followed != null && masters.Contains(followed))
                    _ = RepaintAsync(host);
            }
        };
        return timer;
    }

    private async Task RepaintAsync(DeviceHost host)
    {
        // A device still starting up paints its first page itself, with the link already in place.
        if (!_coordinator.IsReady(host)) return;

        try
        {
            // The clip first, so the repaint below goes into its frames when one plays.
            host.Provider.GetRequiredService<IWallpaperAnimationManager>().Refresh();
            await host.Controller.RedrawCurrentTouchPage();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Repainting the background of '{host.Device.ScopeKey}' failed: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// Watches what one host shows as its background: the touch layout on screen (a page, folder or
    /// workspace switch), edits of the current page's wallpaper, and the wallpaper inheritance setting.
    /// </summary>
    private sealed class HostWatch(DeviceHost host, Action changed)
    {
        private TouchButtonPage _page;

        public void Attach()
        {
            host.Controller.PageManager.TouchLayoutChanged += OnTouchLayoutChanged;
            host.Controller.Config.PropertyChanged += OnConfigPropertyChanged;
            WatchPage(host.Controller.Config.CurrentTouchButtonPage);
        }

        public void Detach()
        {
            host.Controller.PageManager.TouchLayoutChanged -= OnTouchLayoutChanged;
            host.Controller.Config.PropertyChanged -= OnConfigPropertyChanged;
            WatchPage(null);
        }

        private void OnTouchLayoutChanged() => OnUiThread(() =>
        {
            WatchPage(host.Controller.Config.CurrentTouchButtonPage);
            changed();
        });

        private void OnConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoupedeckConfig.InheritWallpaperFromPreviousPage))
                changed();
        }

        private void OnPagePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TouchButtonPage.WallpaperInvalidated))
                changed();
        }

        private void WatchPage(TouchButtonPage page)
        {
            if (ReferenceEquals(_page, page)) return;

            if (_page != null) _page.PropertyChanged -= OnPagePropertyChanged;
            _page = page;
            if (_page != null) _page.PropertyChanged += OnPagePropertyChanged;
        }
    }
}
