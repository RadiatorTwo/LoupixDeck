using LoupixDeck.Commands.Base;
using LoupixDeck.Models.Companion;
using LoupixDeck.Registry;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.Commands;

// Commands a master runs on its whole companion group (issue #232). Like the paging commands they are
// hidden: CompanionMenuContributor offers them on an active master, and CompanionCommandPolicy refuses
// every Companion.* command on any other device.

[Command("Companion.PauseGroup", "Pause Companion Group", "Companions",
    Hidden = true,
    Description = "Companions stop following the master until the group is resumed")]
public sealed class PauseCompanionGroupCommand(ICompanionCoordinator coordinator, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        coordinator.SetPaused(device.ScopeKey, true);
        return Task.CompletedTask;
    }
}

[Command("Companion.ResumeGroup", "Resume Companion Group", "Companions",
    Hidden = true,
    Description = "Companions follow the master again and take its current state")]
public sealed class ResumeCompanionGroupCommand(ICompanionCoordinator coordinator, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        coordinator.SetPaused(device.ScopeKey, false);
        return Task.CompletedTask;
    }
}

[Command("Companion.TogglePause", "Toggle Companion Group Pause", "Companions",
    Hidden = true,
    Description = "Pause the companion group, or resume it when paused")]
public sealed class ToggleCompanionGroupPauseCommand(ICompanionCoordinator coordinator, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        coordinator.SetPaused(device.ScopeKey, !coordinator.IsPaused(device.ScopeKey));
        return Task.CompletedTask;
    }
}

[Command("Companion.Resync", "Resync Companions", "Companions",
    Hidden = true,
    Description = "Rebuild the companions' profiles and workspaces from the master and let them follow it again")]
public sealed class ResyncCompanionsCommand(ICompanionContextSync contextSync, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        contextSync.Resync(device.ScopeKey);
        return Task.CompletedTask;
    }
}

[Command("Companion.SetPageFollow", "Set Companion Page Follow", "Companions",
    parameterTemplate: "({Mode})",
    parameterNames: ["Mode"],
    parameterTypes: [typeof(CompanionPageFollowMode)],
    Hidden = true,
    Description = "Choose whether the companions page along with the master")]
public sealed class SetCompanionPageFollowCommand(ICompanionCoordinator coordinator, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !Enum.TryParse(parameters[0].Trim(), ignoreCase: true, out CompanionPageFollowMode mode) ||
            !Enum.IsDefined(mode))
        {
            Console.WriteLine("Usage: Companion.SetPageFollow(Off | TouchPages | TouchAndRotaryPages)");
            return Task.CompletedTask;
        }

        CompanionGroupCommandArgs.SetPageFollow(coordinator, device, mode);
        return Task.CompletedTask;
    }
}

[Command("Companion.CyclePageFollow", "Cycle Companion Page Follow", "Companions",
    Hidden = true,
    Description = "Switch page follow to the next mode: off, touch pages, touch and rotary pages")]
public sealed class CycleCompanionPageFollowCommand(ICompanionCoordinator coordinator, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        CompanionPageFollowMode current = coordinator.FindGroup(device.ScopeKey)?.PageFollow ?? CompanionPageFollowMode.Off;
        CompanionPageFollowMode next = current switch
        {
            CompanionPageFollowMode.Off => CompanionPageFollowMode.TouchPages,
            CompanionPageFollowMode.TouchPages => CompanionPageFollowMode.TouchAndRotaryPages,
            _ => CompanionPageFollowMode.Off
        };

        CompanionGroupCommandArgs.SetPageFollow(coordinator, device, next);
        return Task.CompletedTask;
    }
}

[Command("Companion.ShowStartPages", "Show Companion Start Pages", "Companions",
    Hidden = true,
    Description = "Show the start page of the current workspace on every companion")]
public sealed class ShowCompanionStartPagesCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) => navigation.ShowStartPages(device.ScopeKey);
}

internal static class CompanionGroupCommandArgs
{
    /// <summary>Sets the page follow mode of the group the device leads; no-op unless it is an active master.</summary>
    public static void SetPageFollow(ICompanionCoordinator coordinator, ResolvedDevice device, CompanionPageFollowMode mode)
    {
        if (!coordinator.IsMaster(device.ScopeKey) || coordinator.FindGroup(device.ScopeKey) is not { } group) return;
        coordinator.SetPageFollow(group.Id, mode);
    }
}
