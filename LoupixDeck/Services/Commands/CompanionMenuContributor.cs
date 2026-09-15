using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services.Companion;
// Both the app and the plugin SDK define IMenuContributor — this contributor implements the app-side one.
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Lists a master's group commands (pause, resync, page follow, start pages) and its companions in the
/// "Companions" command group, each companion with its paging commands and its pages per profile and workspace, so the picker offers "Touch page 2: Scenes" instead of raw
/// device keys and page ids. Offline companions are listed from their config file, so their commands
/// can be assigned while they are unplugged. Offered on an active master only.
/// </summary>
public sealed class CompanionMenuContributor(
    IGroupCatalog groupCatalog,
    ICompanionCoordinator companions,
    ResolvedDevice device) : IMenuContributor
{
    public const string GroupName = "Companions";

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        IReadOnlyList<string> companionKeys = companions.IsMaster(device.ScopeKey)
            ? companions.GetCompanionKeys(device.ScopeKey)
            : [];
        if (companionKeys.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        GroupInfo info = groupCatalog.Resolve(GroupName);
        MenuEntry group = new(GroupName, string.Empty)
        {
            Icon = info.Icon,
            Description = info.Description,
            Section = info.Section
        };

        group.Children.Add(BuildGroupCommands(info));
        foreach (string companionKey in companionKeys)
            group.Children.Add(BuildCompanion(companionKey, info));

        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }

    private static MenuEntry BuildGroupCommands(GroupInfo info)
    {
        MenuEntry folder = new(Loc.Tr("CompanionMenu_WholeGroup"), string.Empty)
        {
            Icon = info.Icon,
            Section = info.Section
        };

        foreach ((string name, string command) in new[]
                 {
                     ("Toggle Companion Group Pause", "Companion.TogglePause"),
                     ("Pause Companion Group", "Companion.PauseGroup"),
                     ("Resume Companion Group", "Companion.ResumeGroup"),
                     ("Cycle Companion Page Follow", "Companion.CyclePageFollow"),
                     ("Show Companion Start Pages", "Companion.ShowStartPages"),
                     ("Resync Companions", "Companion.Resync")
                 })
        {
            folder.Children.Add(new MenuEntry(name, command) { Icon = info.Icon });
        }

        foreach ((CompanionPageFollowMode mode, string labelKey) in new[]
                 {
                     (CompanionPageFollowMode.Off, "Companions_PageFollowOff"),
                     (CompanionPageFollowMode.TouchPages, "Companions_PageFollowTouch"),
                     (CompanionPageFollowMode.TouchAndRotaryPages, "Companions_PageFollowTouchAndRotary")
                 })
        {
            folder.Children.Add(new MenuEntry(Loc.Tr("CompanionMenu_SetPageFollowFmt", Loc.Tr(labelKey)), "Companion.SetPageFollow")
            {
                Icon = info.Icon,
                Parameters = new Dictionary<string, string> { ["Mode"] = mode.ToString() }
            });
        }

        return folder;
    }

    private MenuEntry BuildCompanion(string companionKey, GroupInfo info)
    {
        string name = companions.GetDisplayName(companionKey);
        MenuEntry folder = new(companions.IsOnline(companionKey) ? name : Loc.Tr("CompanionMenu_OfflineDeviceFmt", name), string.Empty)
        {
            Icon = info.Icon,
            Section = info.Section
        };

        LoupedeckConfig config = companions.GetDeviceConfig(companionKey);
        bool hasSideStrips = CompanionDeviceTraits.HasSideStrips(config);

        folder.Children.Add(Step("Next Touch Page", "Companion.NextTouchPage", companionKey, info));
        folder.Children.Add(Step("Previous Touch Page", "Companion.PreviousTouchPage", companionKey, info));
        if (!hasSideStrips)
        {
            folder.Children.Add(Step("Next Rotary Page", "Companion.NextRotaryPage", companionKey, info));
            folder.Children.Add(Step("Previous Rotary Page", "Companion.PreviousRotaryPage", companionKey, info));
        }

        if (config?.Profiles == null)
            return folder;

        foreach (Profile profile in config.Profiles)
        {
            if (profile.Workspaces == null) continue;

            foreach (Workspace workspace in profile.Workspaces)
            {
                MenuEntry pages = new($"{Display(profile.Name)} / {Display(workspace.Name)}", string.Empty)
                {
                    Icon = info.Icon,
                    Section = info.Section
                };

                AddPages(pages, workspace.TouchButtonPages, "CompanionMenu_TouchPageFmt", "Companion.GotoTouchPage", companionKey, info);
                if (hasSideStrips)
                {
                    AddPages(pages, workspace.LeftRotaryButtonPages, "CompanionMenu_LeftRotaryPageFmt", "Companion.GotoRotaryPageLeft", companionKey, info);
                    AddPages(pages, workspace.RightRotaryButtonPages, "CompanionMenu_RightRotaryPageFmt", "Companion.GotoRotaryPageRight", companionKey, info);
                }
                else
                {
                    AddPages(pages, workspace.RotaryButtonPages, "CompanionMenu_RotaryPageFmt", "Companion.GotoRotaryPage", companionKey, info);
                }

                if (pages.Children.Count > 0)
                    folder.Children.Add(pages);
            }
        }

        return folder;
    }

    private static MenuEntry Step(string displayName, string command, string companionKey, GroupInfo info) =>
        new(displayName, command)
        {
            Icon = info.Icon,
            Parameters = new Dictionary<string, string> { ["Device"] = companionKey }
        };

    private static void AddPages<TPage>(MenuEntry folder, IList<TPage> pages, string labelKey, string command,
        string companionKey, GroupInfo info)
        where TPage : ButtonPageBase
    {
        if (pages == null) return;

        for (int i = 0; i < pages.Count; i++)
        {
            folder.Children.Add(new MenuEntry(CompanionDeviceTraits.PageLabel(labelKey, i, pages[i].Name), command)
            {
                Icon = info.Icon,
                Parameters = new Dictionary<string, string>
                {
                    ["Device"] = companionKey,
                    ["Page"] = pages[i].Id.ToString()
                }
            });
        }
    }

    private static string Display(string name) =>
        string.IsNullOrWhiteSpace(name) ? Loc.Tr("CompanionMenu_Unnamed") : name;
}
