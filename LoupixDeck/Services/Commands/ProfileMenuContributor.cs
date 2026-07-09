using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
// Both the app and the plugin SDK define IMenuContributor — this contributor implements the app-side one.
using IMenuContributor = LoupixDeck.Services.Commands.IMenuContributor;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Lists concrete profiles and workspaces in the "Profiles" command group (issue #132), so the
/// picker offers "Activate: OBS" instead of a raw profile-id field. Each leaf carries the target's
/// Guid in <see cref="MenuEntry.Parameters"/>, which <see cref="CommandBuilder"/> substitutes into
/// the (hidden) <c>System.ActivateProfile</c> / <c>System.GotoWorkspace</c> commands. Merges into
/// the same "Profiles" group as the static Next/Previous/Home workspace commands.
/// </summary>
public class ProfileMenuContributor(LoupedeckConfig config, IGroupCatalog groupCatalog) : IMenuContributor
{
    public const string GroupName = "Profiles";

    public Task<IReadOnlyList<MenuEntry>> Contribute(ButtonTargets target)
    {
        var profiles = config.Profiles;
        if (profiles == null || profiles.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuEntry>>([]);

        var info = groupCatalog.Resolve(GroupName);
        var group = new MenuEntry(GroupName, string.Empty)
        {
            Icon = info.Icon,
            Description = info.Description,
            Section = info.Section
        };

        // One "Activate: <profile>" leaf per profile.
        foreach (var profile in profiles)
        {
            group.Children.Add(new MenuEntry($"Activate: {Display(profile.Name)}", "System.ActivateProfile")
            {
                Icon = info.Icon,
                Parameters = new Dictionary<string, string> { ["Profile"] = profile.Id.ToString() }
            });
        }

        // Per-profile workspace folders (GotoWorkspace applies within the active profile).
        foreach (var profile in profiles)
        {
            if (profile.Workspaces == null || profile.Workspaces.Count == 0)
                continue;

            var folder = new MenuEntry($"{Display(profile.Name)} workspaces", string.Empty)
            {
                Icon = info.Icon,
                Section = info.Section
            };

            foreach (var workspace in profile.Workspaces)
            {
                folder.Children.Add(new MenuEntry(Display(workspace.Name), "System.GotoWorkspace")
                {
                    Icon = info.Icon,
                    Parameters = new Dictionary<string, string> { ["Workspace"] = workspace.Id.ToString() }
                });
            }

            group.Children.Add(folder);
        }

        return Task.FromResult<IReadOnlyList<MenuEntry>>([group]);
    }

    private static string Display(string name) => string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
}
