using LoupixDeck.Controllers;
using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// Per-device coordinator for activating profiles and workspaces (issue #132). Activating a
/// profile opens its home workspace; activating a workspace switches the active page set within
/// the current profile. Each switch updates the config's active ids, rebinds the active-workspace
/// facade, and repaints the device (via <see cref="IDeviceController.ApplyActiveWorkspace"/>).
/// </summary>
public interface IWorkspaceActivationService
{
    /// <summary>The currently active profile (null before the config is initialized).</summary>
    Profile ActiveProfile { get; }

    /// <summary>The currently active workspace within the active profile.</summary>
    Workspace ActiveWorkspace { get; }

    /// <summary>Raised after the active profile changes.</summary>
    event Action<Profile> ActiveProfileChanged;

    /// <summary>Raised after the active workspace changes (including as part of a profile switch).</summary>
    event Action<Workspace> ActiveWorkspaceChanged;

    /// <summary>Activates the profile with the given id and opens its home workspace. No-op when the
    /// id does not resolve.</summary>
    Task ActivateProfile(Guid profileId);

    /// <summary>Activates the workspace with the given id within the active profile. No-op when the
    /// id does not resolve or is already active.</summary>
    Task ActivateWorkspace(Guid workspaceId);

    /// <summary>Returns to the active profile's home workspace.</summary>
    Task GoToHomeWorkspace();

    /// <summary>Activates the next workspace in the active profile (wraps).</summary>
    Task NextWorkspace();

    /// <summary>Activates the previous workspace in the active profile (wraps).</summary>
    Task PreviousWorkspace();
}

public sealed class WorkspaceActivationService(LoupedeckConfig config, IDeviceController controller)
    : IWorkspaceActivationService
{
    public Profile ActiveProfile => config.ActiveProfile;
    public Workspace ActiveWorkspace => config.ActiveWorkspace;

    public event Action<Profile> ActiveProfileChanged;
    public event Action<Workspace> ActiveWorkspaceChanged;

    public async Task ActivateProfile(Guid profileId)
    {
        var profile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
        {
            Console.WriteLine($"ActivateProfile: profile {profileId} not found.");
            return;
        }

        // Setting the active ids rebinds the config facade (OnActive*IdChanged → RebindActiveWorkspace).
        config.ActiveProfileId = profile.Id;
        config.ActiveWorkspaceId = profile.HomeWorkspace?.Id ?? Guid.Empty;

        await controller.ApplyActiveWorkspace();

        ActiveProfileChanged?.Invoke(profile);
        ActiveWorkspaceChanged?.Invoke(ActiveWorkspace);
    }

    public async Task ActivateWorkspace(Guid workspaceId)
    {
        var profile = config.ActiveProfile;
        var workspace = profile?.Workspaces?.FirstOrDefault(w => w.Id == workspaceId);
        if (workspace == null)
        {
            Console.WriteLine($"ActivateWorkspace: workspace {workspaceId} not found in the active profile.");
            return;
        }

        if (config.ActiveWorkspaceId == workspaceId)
            return;

        config.ActiveWorkspaceId = workspaceId;

        await controller.ApplyActiveWorkspace();

        ActiveWorkspaceChanged?.Invoke(workspace);
    }

    public Task GoToHomeWorkspace()
    {
        var home = config.ActiveProfile?.HomeWorkspace;
        return home == null ? Task.CompletedTask : ActivateWorkspace(home.Id);
    }

    public Task NextWorkspace() => StepWorkspace(+1);
    public Task PreviousWorkspace() => StepWorkspace(-1);

    private Task StepWorkspace(int direction)
    {
        var profile = config.ActiveProfile;
        var workspaces = profile?.Workspaces;
        if (workspaces == null || workspaces.Count <= 1)
            return Task.CompletedTask;

        var current = workspaces.ToList().FindIndex(w => w.Id == config.ActiveWorkspaceId);
        if (current < 0) current = 0;

        var next = (((current + direction) % workspaces.Count) + workspaces.Count) % workspaces.Count;
        return ActivateWorkspace(workspaces[next].Id);
    }
}
