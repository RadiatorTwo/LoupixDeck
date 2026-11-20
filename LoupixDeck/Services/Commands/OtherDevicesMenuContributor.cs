using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services.Companion;
// Both the app and the plugin SDK define IMenuContributor — this contributor implements the app-side one.
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Lists every other known device in the "Other Devices" command group (issue #666), each with its
/// paging and workspace commands and its profiles, workspaces and touch pages, so a button on one
/// device can switch another one without a companion group. Unplugged devices are listed from their
/// config file, so their commands can be assigned while they are away. A companion that follows its
/// master gets no profile or workspace entries, since it would refuse them.
/// </summary>
public sealed class OtherDevicesMenuContributor(
    IGroupCatalog groupCatalog,
    ICompanionCoordinator devices,
    ResolvedDevice device) : IMenuContributor
{
    public const string GroupName = "Other Devices";

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        List<CompanionDeviceInfo> others = devices.GetKnownDevices()
            .Where(d => !string.Equals(d.Key, device.ScopeKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (others.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        GroupInfo info = groupCatalog.Resolve(GroupName);
        MenuEntry group = new(GroupName, string.Empty)
        {
            Icon = info.Icon,
            Description = info.Description,
            Section = info.Section
        };

        foreach (CompanionDeviceInfo other in others)
            group.Children.Add(BuildDevice(other, info));

        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }

    private MenuEntry BuildDevice(CompanionDeviceInfo other, GroupInfo info)
    {
        string key = other.Key;
        MenuEntry folder = new(other.IsOnline ? other.DisplayName : Loc.Tr("CompanionMenu_OfflineDeviceFmt", other.DisplayName), string.Empty)
        {
            Icon = info.Icon,
            Section = info.Section
        };

        folder.Children.Add(Leaf("Next Touch Page", "Device.NextTouchPage", key, info));
        folder.Children.Add(Leaf("Previous Touch Page", "Device.PreviousTouchPage", key, info));
        folder.Children.Add(Leaf("Next Rotary Page", "Device.NextRotaryPage", key, info));
        folder.Children.Add(Leaf("Previous Rotary Page", "Device.PreviousRotaryPage", key, info));

        // A companion that follows its master leaves profile and workspace switching to the master.
        if (devices.IsFollowingMaster(key))
            return folder;

        folder.Children.Add(Leaf("Last Profile", "Device.LastProfile", key, info));
        folder.Children.Add(Leaf("Next Workspace", "Device.NextWorkspace", key, info));
        folder.Children.Add(Leaf("Previous Workspace", "Device.PreviousWorkspace", key, info));
        folder.Children.Add(Leaf("Go to Home Workspace", "Device.GoHomeWorkspace", key, info));

        LoupedeckConfig config = devices.GetDeviceConfig(key);
        if (config?.Profiles == null)
            return folder;

        foreach (Profile profile in config.Profiles)
        {
            folder.Children.Add(Leaf(Loc.Tr("OtherDevicesMenu_ActivateProfileFmt", Display(profile.Name)),
                "Device.ActivateProfile", key, info, ("Profile", profile.Id)));
        }

        foreach (Profile profile in config.Profiles)
        {
            if (profile.Workspaces is not { Count: > 0 }) continue;

            MenuEntry workspaces = new(Loc.Tr("OtherDevicesMenu_ProfileWorkspacesFmt", Display(profile.Name)), string.Empty)
            {
                Icon = info.Icon,
                Section = info.Section
            };

            foreach (Workspace workspace in profile.Workspaces)
                workspaces.Children.Add(BuildWorkspace(workspace, key, info));

            folder.Children.Add(workspaces);
        }

        return folder;
    }

    /// <summary>A workspace folder: opening the workspace itself, then each of its touch pages.</summary>
    private static MenuEntry BuildWorkspace(Workspace workspace, string key, GroupInfo info)
    {
        MenuEntry folder = new(Display(workspace.Name), string.Empty)
        {
            Icon = info.Icon,
            Section = info.Section
        };

        folder.Children.Add(Leaf(Loc.Tr("OtherDevicesMenu_OpenWorkspace"), "Device.GotoWorkspace", key, info,
            ("Workspace", workspace.Id)));

        IList<TouchButtonPage> pages = workspace.TouchButtonPages;
        for (int i = 0; i < (pages?.Count ?? 0); i++)
        {
            folder.Children.Add(Leaf(CompanionDeviceTraits.PageLabel("CompanionMenu_TouchPageFmt", i, pages[i].Name),
                "Device.GotoTouchPage", key, info, ("Page", pages[i].Id)));
        }

        return folder;
    }

    private static MenuEntry Leaf(string displayName, string command, string deviceKey, GroupInfo info,
        (string Name, Guid Id)? extra = null)
    {
        Dictionary<string, string> parameters = new() { ["Device"] = deviceKey };
        if (extra is { } e)
            parameters[e.Name] = e.Id.ToString();

        return new MenuEntry(displayName, command) { Icon = info.Icon, Parameters = parameters };
    }

    private static string Display(string name) =>
        string.IsNullOrWhiteSpace(name) ? Loc.Tr("CompanionMenu_Unnamed") : name;
}
