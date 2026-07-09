using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

// Profile / workspace switching commands (issue #132). Targets are stored as the profile/workspace
// Guid so they survive renames; the command picker shows the friendly name and fills the Guid.

[Command("System.ActivateProfile", "Activate Profile", "Profiles",
    parameterTemplate: "({Profile})",
    parameterNames: ["Profile"],
    parameterTypes: [typeof(string)],
    Description = "Activate a profile and open its home workspace")]
public class ActivateProfileCommand(IWorkspaceActivationService activation) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !Guid.TryParse(parameters[0], out var id))
        {
            Console.WriteLine("Usage: System.ActivateProfile(profileId)");
            return;
        }

        await activation.ActivateProfile(id);
    }
}

[Command("System.GotoWorkspace", "Go to Workspace", "Profiles",
    parameterTemplate: "({Workspace})",
    parameterNames: ["Workspace"],
    parameterTypes: [typeof(string)],
    Description = "Switch to a workspace within the active profile")]
public class GotoWorkspaceCommand(IWorkspaceActivationService activation) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !Guid.TryParse(parameters[0], out var id))
        {
            Console.WriteLine("Usage: System.GotoWorkspace(workspaceId)");
            return;
        }

        await activation.ActivateWorkspace(id);
    }
}

[Command("System.NextWorkspace", "Next Workspace", "Profiles",
    Description = "Switch to the next workspace in the active profile")]
public class NextWorkspaceCommand(IWorkspaceActivationService activation) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return;
        }

        await activation.NextWorkspace();
    }
}

[Command("System.PreviousWorkspace", "Previous Workspace", "Profiles",
    Description = "Switch to the previous workspace in the active profile")]
public class PreviousWorkspaceCommand(IWorkspaceActivationService activation) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return;
        }

        await activation.PreviousWorkspace();
    }
}

[Command("System.GoHomeWorkspace", "Go to Home Workspace", "Profiles",
    Description = "Return to the active profile's home workspace")]
public class GoHomeWorkspaceCommand(IWorkspaceActivationService activation) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return;
        }

        await activation.GoToHomeWorkspace();
    }
}
