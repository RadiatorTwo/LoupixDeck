using LoupixDeck.Localization;
using LoupixDeck.Models;

namespace LoupixDeck.Services.Companion;

/// <summary>A companion that would lose content, with how many of its own pages and LED buttons hold anything.</summary>
public sealed record CompanionLossEntry(string DeviceName, bool IsOnline, int PageCount, int LedButtonCount = 0);

/// <summary>
/// What removing a profile or workspace on a master takes from its companions. A companion's mirror of
/// that profile or workspace goes with it, and so do the companion's own pages inside — and, for a
/// profile, the companion's own LED buttons, which belong to the profile. Only pages and LED buttons that
/// hold something count: the empty page a new workspace starts with is no loss worth a warning.
/// Offline companions are read from their config file.
/// </summary>
public static class CompanionImpact
{
    /// <summary>The companions of <paramref name="masterKey"/> that have content in the profile.
    /// Empty when the device is not an active master.</summary>
    public static IReadOnlyList<CompanionLossEntry> ForProfile(ICompanionCoordinator coordinator, string masterKey, Guid profileId) =>
        Collect(coordinator, masterKey, config =>
        {
            Profile profile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            return (profile?.Workspaces ?? [], profile?.SimpleButtons);
        });

    /// <summary>The companions of <paramref name="masterKey"/> that have content in the workspace.</summary>
    public static IReadOnlyList<CompanionLossEntry> ForWorkspace(ICompanionCoordinator coordinator, string masterKey, Guid workspaceId) =>
        ForWorkspaces(coordinator, masterKey, [workspaceId]);

    /// <summary>The companions of <paramref name="masterKey"/> that have content in any of the workspaces,
    /// leaving out <paramref name="exceptCompanionKeys"/>. Empty without asking any companion when
    /// <paramref name="workspaceIds"/> is empty.</summary>
    public static IReadOnlyList<CompanionLossEntry> ForWorkspaces(ICompanionCoordinator coordinator, string masterKey,
        IReadOnlyCollection<Guid> workspaceIds, IReadOnlyCollection<string> exceptCompanionKeys = null) =>
        workspaceIds.Count == 0
            ? []
            : Collect(coordinator, masterKey, config =>
                (workspaceIds.Select(id => CompanionDeviceTraits.FindWorkspace(config, id)).Where(w => w != null), null),
                exceptCompanionKeys);

    /// <summary>
    /// The companions of <paramref name="masterKey"/> whose own layouts in the given custom folders of
    /// the workspace hold anything (issue #249). Their folder layouts go when the master deletes the folders.
    /// </summary>
    public static IReadOnlyList<CompanionLossEntry> ForFolders(ICompanionCoordinator coordinator, string masterKey,
        Guid workspaceId, IReadOnlySet<Guid> folderIds)
    {
        if (folderIds.Count == 0 || string.IsNullOrEmpty(masterKey) || !coordinator.IsMaster(masterKey))
            return [];

        List<CompanionLossEntry> losses = [];
        foreach (string companionKey in coordinator.GetCompanionKeys(masterKey))
        {
            Workspace workspace = CompanionDeviceTraits.FindWorkspace(coordinator.GetDeviceConfig(companionKey), workspaceId);
            if (workspace == null) continue;

            int layouts = workspace.EnumerateFolders()
                .Where(f => folderIds.Contains(f.Id) && f.Layout != null)
                .Count(f => f.Layout.TouchButtons.Any(b => b != null && !b.IsFolderBackSlot && !ButtonSnapshot.IsEmpty(b)));
            if (layouts > 0)
                losses.Add(new CompanionLossEntry(coordinator.GetDisplayName(companionKey), coordinator.IsOnline(companionKey), layouts));
        }
        return losses;
    }

    /// <summary>One line per companion ("• Razer Stream Controller: 4 page(s), 2 LED button(s)"), or empty.</summary>
    public static string Describe(IReadOnlyList<CompanionLossEntry> losses) =>
        string.Join(Environment.NewLine, losses.Select(loss => Loc.Tr(
            loss.IsOnline ? "Companion_LossLineFmt" : "Companion_LossOfflineLineFmt",
            loss.DeviceName, DescribeCounts(loss))));

    private static string DescribeCounts(CompanionLossEntry loss)
    {
        List<string> parts = [];
        if (loss.PageCount > 0) parts.Add(Loc.Tr("Companion_LossPagesFmt", loss.PageCount));
        if (loss.LedButtonCount > 0) parts.Add(Loc.Tr("Companion_LossLedButtonsFmt", loss.LedButtonCount));
        return string.Join(", ", parts);
    }

    private static IReadOnlyList<CompanionLossEntry> Collect(ICompanionCoordinator coordinator, string masterKey,
        Func<LoupedeckConfig, (IEnumerable<Workspace> Workspaces, SimpleButton[] LedButtons)> contentOf,
        IReadOnlyCollection<string> exceptCompanionKeys = null)
    {
        if (string.IsNullOrEmpty(masterKey) || !coordinator.IsMaster(masterKey))
            return [];

        List<CompanionLossEntry> losses = [];
        foreach (string companionKey in coordinator.GetCompanionKeys(masterKey))
        {
            if (exceptCompanionKeys?.Contains(companionKey, StringComparer.OrdinalIgnoreCase) == true) continue;

            LoupedeckConfig config = coordinator.GetDeviceConfig(companionKey);
            if (config == null) continue;

            (IEnumerable<Workspace> workspaces, SimpleButton[] ledButtons) = contentOf(config);
            int pages = workspaces.Sum(CountPagesWithContent);
            int leds = CountLedButtonsWithContent(ledButtons);
            if (pages > 0 || leds > 0)
                losses.Add(new CompanionLossEntry(coordinator.GetDisplayName(companionKey), coordinator.IsOnline(companionKey), pages, leds));
        }
        return losses;
    }

    /// <summary>Touch layouts (pages and folders), rotary and side-strip pages of the workspace that hold at least one non-empty button.</summary>
    public static int CountPagesWithContent(Workspace workspace) =>
        Count(workspace.EnumerateTouchLayouts(), page => page.TouchButtons) +
        Count(workspace.RotaryButtonPages, page => page.RotaryButtons) +
        Count(workspace.LeftRotaryButtonPages, page => page.RotaryButtons) +
        Count(workspace.RightRotaryButtonPages, page => page.RotaryButtons);

    /// <summary>LED buttons that hold anything.</summary>
    public static int CountLedButtonsWithContent(IEnumerable<SimpleButton> ledButtons) =>
        ledButtons?.Count(button => button != null && !ButtonSnapshot.IsEmpty(button)) ?? 0;

    private static int Count<TPage, TButton>(IEnumerable<TPage> pages, Func<TPage, IEnumerable<TButton>> buttonsOf)
        where TButton : LoupedeckButton =>
        pages?.Count(page => buttonsOf(page).Any(button => !ButtonSnapshot.IsEmpty(button))) ?? 0;
}
