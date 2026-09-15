using LoupixDeck.Localization;
using LoupixDeck.Models;

namespace LoupixDeck.Services.Companion;

/// <summary>A companion that would lose pages, with how many of its own pages hold anything.</summary>
public sealed record CompanionLossEntry(string DeviceName, bool IsOnline, int PageCount);

/// <summary>
/// What removing a profile or workspace on a master takes from its companions. A companion's mirror of
/// that profile or workspace goes with it, and so do the companion's own pages inside. Only pages that
/// hold something count: the empty page a new workspace starts with is no loss worth a warning.
/// Offline companions are read from their config file.
/// </summary>
public static class CompanionImpact
{
    /// <summary>The companions of <paramref name="masterKey"/> that have content in the profile.
    /// Empty when the device is not an active master.</summary>
    public static IReadOnlyList<CompanionLossEntry> ForProfile(ICompanionCoordinator coordinator, string masterKey, Guid profileId) =>
        Collect(coordinator, masterKey, config =>
            config.Profiles?.FirstOrDefault(p => p.Id == profileId)?.Workspaces ?? []);

    /// <summary>The companions of <paramref name="masterKey"/> that have content in the workspace.</summary>
    public static IReadOnlyList<CompanionLossEntry> ForWorkspace(ICompanionCoordinator coordinator, string masterKey, Guid workspaceId) =>
        Collect(coordinator, masterKey, config =>
            CompanionDeviceTraits.FindWorkspace(config, workspaceId) is { } workspace ? [workspace] : []);

    /// <summary>One line per companion ("• Razer Stream Controller: 4 pages"), or empty.</summary>
    public static string Describe(IReadOnlyList<CompanionLossEntry> losses) =>
        string.Join(Environment.NewLine, losses.Select(loss => Loc.Tr(
            loss.IsOnline ? "Companion_PagesLostLineFmt" : "Companion_PagesLostOfflineLineFmt",
            loss.DeviceName, loss.PageCount)));

    private static IReadOnlyList<CompanionLossEntry> Collect(ICompanionCoordinator coordinator, string masterKey,
        Func<LoupedeckConfig, IEnumerable<Workspace>> workspacesOf)
    {
        if (string.IsNullOrEmpty(masterKey) || !coordinator.IsMaster(masterKey))
            return [];

        List<CompanionLossEntry> losses = [];
        foreach (string companionKey in coordinator.GetCompanionKeys(masterKey))
        {
            LoupedeckConfig config = coordinator.GetDeviceConfig(companionKey);
            if (config == null) continue;

            int pages = workspacesOf(config).Sum(CountPagesWithContent);
            if (pages > 0)
                losses.Add(new CompanionLossEntry(coordinator.GetDisplayName(companionKey), coordinator.IsOnline(companionKey), pages));
        }
        return losses;
    }

    private static int CountPagesWithContent(Workspace workspace) =>
        Count(workspace.TouchButtonPages, page => page.TouchButtons) +
        Count(workspace.RotaryButtonPages, page => page.RotaryButtons) +
        Count(workspace.LeftRotaryButtonPages, page => page.RotaryButtons) +
        Count(workspace.RightRotaryButtonPages, page => page.RotaryButtons);

    private static int Count<TPage, TButton>(IEnumerable<TPage> pages, Func<TPage, IEnumerable<TButton>> buttonsOf)
        where TButton : LoupedeckButton =>
        pages?.Count(page => buttonsOf(page).Any(button => !ButtonSnapshot.IsEmpty(button))) ?? 0;
}
