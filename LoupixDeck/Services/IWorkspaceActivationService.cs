using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.Services;

/// <summary>
/// Per-device coordinator for activating profiles and workspaces (issue #132). Activating a
/// profile opens its home workspace; activating a workspace switches the active page set within
/// the current profile. Each switch updates the config's active ids, rebinds the active-workspace
/// facade, and repaints the device (via <see cref="IDeviceController.ApplyActiveWorkspace"/>).
/// On a companion the master owns the profile and workspace: every switch is refused except
/// <see cref="FollowMaster"/>.
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

    /// <summary>Raised when a <em>manual</em> activation happens (a command, button, or the settings
    /// UI — <c>manual: true</c>). The context engine uses this to suspend automatic switching until
    /// the foreground app changes. Not raised for its own automatic (<c>manual: false</c>) switches.</summary>
    event Action ManualActivation;

    /// <summary>Activates the profile with the given id and opens its home workspace. No-op when the
    /// id does not resolve. <paramref name="manual"/> marks a user-initiated switch (pins auto-switching).</summary>
    Task ActivateProfile(Guid profileId, bool manual = true);

    /// <summary>Activates the workspace with the given id within the active profile. No-op when the
    /// id does not resolve or is already active. <paramref name="manual"/> pins auto-switching.</summary>
    Task ActivateWorkspace(Guid workspaceId, bool manual = true);

    /// <summary>Returns to the active profile's home workspace.</summary>
    Task GoToHomeWorkspace(bool manual = true);

    /// <summary>Activates the next workspace in the active profile (wraps).</summary>
    Task NextWorkspace(bool manual = true);

    /// <summary>Activates the previous workspace in the active profile (wraps).</summary>
    Task PreviousWorkspace(bool manual = true);

    /// <summary>
    /// Moves a companion to the profile and workspace its master shows, in one repaint. Ids that do
    /// not resolve fall back to the first profile and the profile's home workspace. No-op when both
    /// are already active. The only switch allowed on a companion; never counts as manual.
    /// </summary>
    Task FollowMaster(Guid profileId, Guid workspaceId);
}

public sealed class WorkspaceActivationService(
    LoupedeckConfig config,
    IDeviceController controller,
    ICompanionCoordinator companions,
    ResolvedDevice device) : IWorkspaceActivationService
{
    public Profile ActiveProfile => config.ActiveProfile;
    public Workspace ActiveWorkspace => config.ActiveWorkspace;

    public event Action<Profile> ActiveProfileChanged;
    public event Action<Workspace> ActiveWorkspaceChanged;
    public event Action ManualActivation;

    public async Task ActivateProfile(Guid profileId, bool manual = true)
    {
        if (IsFollowingMaster(nameof(ActivateProfile))) return;

        var profile = config.Profiles?.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
        {
            Console.WriteLine($"ActivateProfile: profile {profileId} not found.");
            return;
        }

        if (manual) ManualActivation?.Invoke();

        // Setting the active ids rebinds the config facade (OnActive*IdChanged → RebindActiveWorkspace).
        config.ActiveProfileId = profile.Id;
        config.ActiveWorkspaceId = profile.HomeWorkspace?.Id ?? Guid.Empty;

        // The round LED buttons belong to the profile (config v12), so they are rebuilt here and
        // not in ApplyActiveWorkspace — a workspace switch keeps the same set.
        await controller.ApplyActiveProfileButtons();

        await controller.ApplyActiveWorkspace();

        ActiveProfileChanged?.Invoke(profile);
        ActiveWorkspaceChanged?.Invoke(ActiveWorkspace);
    }

    public async Task ActivateWorkspace(Guid workspaceId, bool manual = true)
    {
        if (IsFollowingMaster(nameof(ActivateWorkspace))) return;

        var profile = config.ActiveProfile;
        var workspace = profile?.Workspaces?.FirstOrDefault(w => w.Id == workspaceId);
        if (workspace == null)
        {
            Console.WriteLine($"ActivateWorkspace: workspace {workspaceId} not found in the active profile.");
            return;
        }

        if (manual) ManualActivation?.Invoke();

        if (config.ActiveWorkspaceId == workspaceId)
            return;

        config.ActiveWorkspaceId = workspaceId;

        await controller.ApplyActiveWorkspace();

        ActiveWorkspaceChanged?.Invoke(workspace);
    }

    public Task GoToHomeWorkspace(bool manual = true)
    {
        var home = config.ActiveProfile?.HomeWorkspace;
        return home == null ? Task.CompletedTask : ActivateWorkspace(home.Id, manual);
    }

    public async Task FollowMaster(Guid profileId, Guid workspaceId)
    {
        var profile = config.Profiles?.FirstOrDefault(p => p.Id == profileId) ?? config.Profiles?.FirstOrDefault();
        if (profile == null) return;
        var workspace = profile.Workspaces?.FirstOrDefault(w => w.Id == workspaceId) ?? profile.HomeWorkspace;

        var targetWorkspaceId = workspace?.Id ?? Guid.Empty;
        var profileChanged = config.ActiveProfileId != profile.Id;
        if (!profileChanged && config.ActiveWorkspaceId == targetWorkspaceId)
            return;

        config.ActiveProfileId = profile.Id;
        config.ActiveWorkspaceId = targetWorkspaceId;

        if (profileChanged)
            await controller.ApplyActiveProfileButtons();

        await controller.ApplyActiveWorkspace();

        if (profileChanged)
            ActiveProfileChanged?.Invoke(profile);
        ActiveWorkspaceChanged?.Invoke(ActiveWorkspace);
    }

    /// <summary>True (and logged) when this device is a companion, whose master owns the switch.</summary>
    private bool IsFollowingMaster(string operation)
    {
        if (!companions.IsCompanion(device.ScopeKey)) return false;
        Console.WriteLine($"{operation} skipped: '{device.ScopeKey}' is a companion and follows its master.");
        return true;
    }

    public Task NextWorkspace(bool manual = true) => StepWorkspace(+1, manual);
    public Task PreviousWorkspace(bool manual = true) => StepWorkspace(-1, manual);

    private Task StepWorkspace(int direction, bool manual)
    {
        var profile = config.ActiveProfile;
        var workspaces = profile?.Workspaces;
        if (workspaces == null || workspaces.Count <= 1)
            return Task.CompletedTask;

        var current = workspaces.ToList().FindIndex(w => w.Id == config.ActiveWorkspaceId);
        if (current < 0) current = 0;

        var next = (((current + direction) % workspaces.Count) + workspaces.Count) % workspaces.Count;
        return ActivateWorkspace(workspaces[next].Id, manual);
    }
}
