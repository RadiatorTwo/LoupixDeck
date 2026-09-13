using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// Per-device editing of the profile/workspace tree (add and remove). Shared by the Settings pane
/// and the main window header so both keep the startup, home and active ids valid the same way.
/// Renaming needs no service: <see cref="Profile.Name"/> and <see cref="Workspace.Name"/> are
/// observable and are set directly.
/// </summary>
public interface IProfileEditingService
{
    /// <summary>Adds a profile with one "Home" workspace. Does not activate it.</summary>
    Profile AddProfile(string name);

    /// <summary>True when the profile exists and is not the last one on the device.</summary>
    bool CanRemoveProfile(Profile profile);

    /// <summary>Removes the profile. Moves the startup profile and, when the removed profile was
    /// active, activates the first remaining one. False when nothing was removed.</summary>
    Task<bool> RemoveProfile(Profile profile);

    /// <summary>Adds a workspace to the profile. Does not activate it.</summary>
    Workspace AddWorkspace(Profile profile, string name);

    /// <summary>True when the workspace belongs to the profile and is not its last one.</summary>
    bool CanRemoveWorkspace(Profile profile, Workspace workspace);

    /// <summary>Removes the workspace. Moves the home workspace and, when the removed workspace was
    /// active, returns to the home workspace. False when nothing was removed.</summary>
    Task<bool> RemoveWorkspace(Profile profile, Workspace workspace);
}

public sealed class ProfileEditingService(LoupedeckConfig config, IWorkspaceActivationService activation)
    : IProfileEditingService
{
    public Profile AddProfile(string name)
    {
        // SimpleButtons is deliberately left null: the controller builds the device defaults when
        // the profile is first activated (ApplyActiveProfileButtons). Copying the current profile's
        // LED buttons here would make "add profile" quietly duplicate someone else's LED state.
        Workspace workspace = new() { Name = "Home" };
        Profile profile = new() { Name = name, HomeWorkspaceId = workspace.Id };
        profile.Workspaces.Add(workspace);
        config.Profiles.Add(profile);
        return profile;
    }

    public bool CanRemoveProfile(Profile profile) =>
        profile != null && config.Profiles.Count > 1 && config.Profiles.Contains(profile);

    public async Task<bool> RemoveProfile(Profile profile)
    {
        if (!CanRemoveProfile(profile))
            return false;

        bool wasActive = config.ActiveProfileId == profile.Id;
        bool wasStartup = config.StartupProfileId == profile.Id;

        config.Profiles.Remove(profile);

        if (wasStartup)
            config.StartupProfileId = config.Profiles[0].Id;

        if (wasActive)
            await activation.ActivateProfile(config.Profiles[0].Id);

        return true;
    }

    public Workspace AddWorkspace(Profile profile, string name)
    {
        if (profile == null)
            return null;

        Workspace workspace = new() { Name = name };
        profile.Workspaces.Add(workspace);
        return workspace;
    }

    public bool CanRemoveWorkspace(Profile profile, Workspace workspace) =>
        profile?.Workspaces != null && workspace != null
        && profile.Workspaces.Count > 1 && profile.Workspaces.Contains(workspace);

    public async Task<bool> RemoveWorkspace(Profile profile, Workspace workspace)
    {
        if (!CanRemoveWorkspace(profile, workspace))
            return false;

        bool wasHome = profile.HomeWorkspaceId == workspace.Id;
        bool wasActive = config.ActiveProfileId == profile.Id && config.ActiveWorkspaceId == workspace.Id;

        profile.Workspaces.Remove(workspace);

        if (wasHome)
            profile.HomeWorkspaceId = profile.Workspaces[0].Id;

        if (wasActive)
            await activation.GoToHomeWorkspace();

        return true;
    }
}
