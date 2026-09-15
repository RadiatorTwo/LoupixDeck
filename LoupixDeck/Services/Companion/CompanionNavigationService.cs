using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Lets a master page through its companions' pages. Profile and workspace stay shared, so every
/// target is resolved inside the workspace the companion currently shows; nothing here switches a
/// companion's profile or workspace.
///
/// Only a master may drive its own companions, checked on every call. A companion that is offline,
/// turned off, or whose screen is owned by a plugin folder or exclusive mode is left alone, and
/// nothing is buffered for later.
/// Root-level singleton; all work runs on the UI thread.
/// </summary>
public interface ICompanionNavigation
{
    /// <summary>Opens the target's pages on the companion it names, with the usual page transition.
    /// True when at least one page resolved and was shown.</summary>
    Task<bool> GotoPage(string masterKey, CompanionPageTarget target);

    /// <summary>Pages the companion one step forward or back, with the usual page transition.</summary>
    Task<bool> StepPage(string masterKey, string companionKey, CompanionPageKind kind, bool next);

    /// <summary>
    /// Opens the target's pages once the companion shows <paramref name="workspaceId"/>. A master that
    /// just switched workspace sees its companions follow asynchronously; a page opened right away
    /// would land in the old workspace or be replaced by the new workspace's start page. When the
    /// companion is already there and not following, the pages open immediately.
    /// </summary>
    Task SetArrivalTarget(string masterKey, Guid workspaceId, CompanionPageTarget target);

    /// <summary>Called by the context sync before a companion follows its master. Drops any arrival
    /// target from an earlier switch.</summary>
    void BeginFollow(string companionKey);

    /// <summary>Called by the context sync after a companion followed its master. Applies a pending
    /// arrival target for the workspace it now shows.</summary>
    Task EndFollow(DeviceHost companionHost);
}

public sealed class CompanionNavigationService : ICompanionNavigation
{
    private readonly ICompanionCoordinator _coordinator;

    // UI thread only. Follows in progress per companion key (a reconnect can overlap a switch).
    private readonly Dictionary<string, int> _following = new(StringComparer.OrdinalIgnoreCase);

    // UI thread only. The target waiting for a companion to reach a workspace, per companion key.
    private readonly Dictionary<string, (Guid WorkspaceId, CompanionPageTarget Target)> _arrivals =
        new(StringComparer.OrdinalIgnoreCase);

    public CompanionNavigationService(ICompanionCoordinator coordinator, IDeviceHostRegistry registry)
    {
        _coordinator = coordinator;

        registry.HostRemoved += host => OnUiThread(() => _arrivals.Remove(host.Device.ScopeKey));
        _coordinator.GroupsChanged += () => OnUiThread(_arrivals.Clear);
    }

    // ── Commands ────────────────────────────────────────────────────────────

    public Task<bool> GotoPage(string masterKey, CompanionPageTarget target) => OnUiThread(async () =>
    {
        if (target == null) return false;

        DeviceHost host = ResolveDrivableCompanion(masterKey, target.DeviceKey, "open a page on");
        return host != null && await ApplyTarget(host, target, animate: true);
    });

    public Task<bool> StepPage(string masterKey, string companionKey, CompanionPageKind kind, bool next) =>
        OnUiThread(() =>
        {
            DeviceHost host = ResolveDrivableCompanion(masterKey, companionKey, "page");
            return Task.FromResult(host != null && Step(host.Controller, kind, next));
        });

    // ── Arrival targets ─────────────────────────────────────────────────────

    public Task SetArrivalTarget(string masterKey, Guid workspaceId, CompanionPageTarget target) => OnUiThread(async () =>
    {
        if (target == null) return false;

        DeviceHost host = ResolveDrivableCompanion(masterKey, target.DeviceKey, "queue a page for");
        if (host == null) return false;

        string companionKey = host.Device.ScopeKey;
        if (!IsFollowing(companionKey) && host.Controller.Config.ActiveWorkspaceId == workspaceId)
        {
            _arrivals.Remove(companionKey);
            return await ApplyTarget(host, target, animate: false);
        }

        _arrivals[companionKey] = (workspaceId, target);
        return true;
    });

    public void BeginFollow(string companionKey)
    {
        if (string.IsNullOrWhiteSpace(companionKey)) return;

        _arrivals.Remove(companionKey);
        _following[companionKey] = _following.GetValueOrDefault(companionKey) + 1;
    }

    public async Task EndFollow(DeviceHost companionHost)
    {
        if (companionHost == null) return;

        string companionKey = companionHost.Device.ScopeKey;
        int remaining = _following.GetValueOrDefault(companionKey) - 1;
        if (remaining > 0)
        {
            _following[companionKey] = remaining;
            return;
        }

        _following.Remove(companionKey);
        if (!_arrivals.Remove(companionKey, out (Guid WorkspaceId, CompanionPageTarget Target) arrival))
            return;

        if (companionHost.Controller.Config.ActiveWorkspaceId != arrival.WorkspaceId ||
            !CanDrive(companionHost))
            return;

        await OnUiThread(() => ApplyTarget(companionHost, arrival.Target, animate: false));
    }

    private bool IsFollowing(string companionKey) => _following.GetValueOrDefault(companionKey) > 0;

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The running host of <paramref name="companionKey"/> when it is a companion of
    /// <paramref name="masterKey"/> and can show a page right now; otherwise null (logged).
    /// </summary>
    private DeviceHost ResolveDrivableCompanion(string masterKey, string companionKey, string operation)
    {
        string resolvedKey = ResolveCompanionKey(masterKey, companionKey);
        if (resolvedKey == null)
        {
            Console.WriteLine($"[Companions] '{masterKey}' cannot {operation} '{companionKey}': not one of its companions.");
            return null;
        }

        DeviceHost host = _coordinator.ResolveHost(resolvedKey);
        if (!CanDrive(host))
        {
            Console.WriteLine($"[Companions] '{masterKey}' cannot {operation} '{resolvedKey}': the companion is not available.");
            return null;
        }

        return host;
    }

    /// <summary>
    /// The companion key of <paramref name="masterKey"/>'s group that <paramref name="deviceKey"/> names.
    /// A stored slug-only key still finds its companion after the coordinator upgraded the group to
    /// the full key, as long as exactly one companion of that model exists.
    /// </summary>
    private string ResolveCompanionKey(string masterKey, string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey) || string.IsNullOrWhiteSpace(deviceKey)) return null;

        IReadOnlyList<string> companions = _coordinator.GetCompanionKeys(masterKey);
        string exact = companions.FirstOrDefault(key => string.Equals(key, deviceKey, StringComparison.OrdinalIgnoreCase));
        if (exact != null || CompanionGroupValidator.HasSerial(deviceKey)) return exact;

        List<string> sameModel = companions
            .Where(key => key.StartsWith(deviceKey + "_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return sameModel.Count == 1 ? sameModel[0] : null;
    }

    /// <summary>True when the host is initialized, connected, switched on and owns its screen.</summary>
    private bool CanDrive(DeviceHost host)
    {
        if (host == null || !_coordinator.IsReady(host)) return false;
        if (!host.Controller.IsDeviceConnected || host.Controller.IsDeviceOff) return false;
        if (host.Provider.GetService<IFolderNavigationService>()?.IsActive == true) return false;
        if (host.Provider.GetService<IExclusiveModeService>()?.IsActive == true) return false;
        return host.Controller.PageManager.TouchButtonPages.Count > 0;
    }

    private static bool Step(IDeviceController controller, CompanionPageKind kind, bool next)
    {
        switch (kind)
        {
            case CompanionPageKind.Touch:
                if (next) controller.AnimateNextTouchPage();
                else controller.AnimatePreviousTouchPage();
                return true;

            case CompanionPageKind.Rotary:
                if (next) controller.AnimateNextRotaryPage();
                else controller.AnimatePreviousRotaryPage();
                return true;

            case CompanionPageKind.RotaryLeft or CompanionPageKind.RotaryRight
                when controller.PageManager.HasIndependentRotarySides:
                controller.AnimateRotaryPageForSide(
                    kind == CompanionPageKind.RotaryLeft ? RotarySide.Left : RotarySide.Right, next);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Shows every page of the target that resolves in the companion's active workspace.</summary>
    private static async Task<bool> ApplyTarget(DeviceHost host, CompanionPageTarget target, bool animate)
    {
        IDeviceController controller = host.Controller;
        IPageManager pages = controller.PageManager;
        bool applied = false;

        if (PageLookup.ResolveIndex(pages.TouchButtonPages, target.TouchPageId, null) is { } touch)
        {
            if (touch != pages.CurrentTouchPageIndex)
            {
                if (animate) controller.AnimateGotoTouchPage(touch);
                else await pages.ApplyTouchPage(touch);
            }
            applied = true;
        }

        if (PageLookup.ResolveIndex(pages.RotaryButtonPages, target.RotaryPageId, null) is { } rotary)
        {
            if (rotary != pages.CurrentRotaryPageIndex)
            {
                if (animate) controller.AnimateGotoRotaryPage(rotary);
                else pages.ApplyRotaryPage(rotary);
            }
            applied = true;
        }

        if (pages.HasIndependentRotarySides)
        {
            applied |= ApplySide(controller, RotarySide.Left, target.LeftRotaryPageId, animate);
            applied |= ApplySide(controller, RotarySide.Right, target.RightRotaryPageId, animate);
        }

        return applied;
    }

    private static bool ApplySide(IDeviceController controller, RotarySide side, Guid? pageId, bool animate)
    {
        IPageManager pages = controller.PageManager;
        if (PageLookup.ResolveIndex(pages.GetRotaryPages(side), pageId, null) is not { } index)
            return false;

        if (index != pages.GetCurrentRotaryPageIndex(side))
        {
            if (animate) controller.AnimateGotoRotaryPageForSide(side, index);
            else pages.ApplyRotaryPage(side, index);
        }
        return true;
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread. A failure is logged and reported as
    /// false, so a command or rule never throws because a companion misbehaved.</summary>
    private static async Task<bool> OnUiThread(Func<Task<bool>> action)
    {
        try
        {
            return Dispatcher.UIThread.CheckAccess()
                ? await action()
                : await Dispatcher.UIThread.InvokeAsync(action);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Companion navigation failed: {ex.Message}");
            return false;
        }
    }
}
