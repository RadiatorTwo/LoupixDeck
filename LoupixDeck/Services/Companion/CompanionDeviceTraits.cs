using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Companion;

/// <summary>A device's button layout as far as its pages are concerned: how many touch and rotary
/// buttons a page holds, and whether the dial columns page separately.</summary>
public sealed record CompanionButtonLayout(int TouchButtonCount, int RotaryButtonCount, bool HasSideStrips)
{
    /// <summary>Knobs on one side page of a side-strip device (3 of the Razer's 6).</summary>
    public int SideRotaryButtonCount => Math.Max(1, RotaryButtonCount / 2);
}

/// <summary>What a companion's config tells about its hardware when the device may be unplugged.</summary>
public static class CompanionDeviceTraits
{
    /// <summary>A device with side strips pages its dial columns separately; its config then holds
    /// left and right rotary pages.</summary>
    public static bool HasSideStrips(LoupedeckConfig config) =>
        config?.Profiles?.Any(p => p.Workspaces?.Any(w => w.LeftRotaryButtonPages?.Count > 0) == true) == true;

    /// <summary>The device behind a scope key, running or only configured; null when unknown.</summary>
    public static ResolvedDevice FindDevice(ICompanionCoordinator coordinator, string deviceKey) =>
        coordinator.ResolveHost(deviceKey)?.Device ??
        ActiveDeviceResolver.EnumerateConfigDevices()
            .FirstOrDefault(d => string.Equals(d.ScopeKey, deviceKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The device's button layout. A connected device reports it; for an unplugged one it is read off
    /// the pages in its config (their button count is the device's) and its model's side strips.
    /// </summary>
    public static CompanionButtonLayout Layout(ICompanionCoordinator coordinator, string deviceKey, LoupedeckConfig config)
    {
        DeviceHost host = coordinator.ResolveHost(deviceKey);
        if (host?.Provider.GetService<IDeviceService>() is { TouchButtonCount: > 0 } deviceService)
        {
            return new CompanionButtonLayout(deviceService.TouchButtonCount, deviceService.RotaryButtonCount,
                host.Provider.GetRequiredService<IPageManager>().HasIndependentRotarySides);
        }

        List<Workspace> workspaces = [.. (config?.Profiles ?? []).Concat(config?.CompanionLink?.OwnProfiles ?? [])
            .SelectMany(p => p.Workspaces ?? [])];

        int touch = workspaces.SelectMany(w => w.TouchButtonPages ?? []).Select(p => p.TouchButtons.Count).DefaultIfEmpty(0).Max();
        int rotary = Math.Max(
            workspaces.SelectMany(w => w.RotaryButtonPages ?? []).Select(p => p.RotaryButtons.Count).DefaultIfEmpty(0).Max(),
            2 * workspaces.SelectMany(w => (w.LeftRotaryButtonPages ?? []).Concat(w.RightRotaryButtonPages ?? []))
                .Select(p => p.RotaryButtons.Count).DefaultIfEmpty(0).Max());
        bool sideStrips = FindDevice(coordinator, deviceKey)?.Info.Geometry.StripWidth > 0 || HasSideStrips(config);

        return new CompanionButtonLayout(touch, rotary, sideStrips);
    }

    /// <summary>The companion's mirror of the workspace with <paramref name="workspaceId"/>, or null.</summary>
    public static Workspace FindWorkspace(LoupedeckConfig config, Guid workspaceId) =>
        config?.Profiles?
            .Where(p => p.Workspaces != null)
            .SelectMany(p => p.Workspaces)
            .FirstOrDefault(w => w.Id == workspaceId);

    /// <summary>"Touch page 2: Scenes" for the page at 0-based <paramref name="index"/>, from the
    /// <paramref name="labelKey"/> format ("Touch page {0}") and the page's optional name.</summary>
    public static string PageLabel(string labelKey, int index, string pageName)
    {
        string label = Loc.Tr(labelKey, index + 1);
        return string.IsNullOrWhiteSpace(pageName) ? label : $"{label}: {pageName}";
    }
}
