using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Companion;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Commands;

// Commands that switch pages, profiles and workspaces on another running device (issue #666), so
// devices that are not in a companion group can still drive each other. The target is named by its
// scope key and runs the matching System.* command through its own command service, so its own rules
// apply: a companion that follows its master still refuses a profile or workspace switch. A target
// that is not running is skipped. All are hidden: the picker offers them per device through
// OtherDevicesMenuContributor.

[Command("Device.NextTouchPage", "Next Touch Page on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the next touch page on another device")]
public sealed class OtherDeviceNextTouchPageCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.NextTouchPage", "System.NextPage");
}

[Command("Device.PreviousTouchPage", "Previous Touch Page on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the previous touch page on another device")]
public sealed class OtherDevicePreviousTouchPageCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.PreviousTouchPage", "System.PreviousPage");
}

[Command("Device.NextRotaryPage", "Next Rotary Page on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the next rotary page on another device")]
public sealed class OtherDeviceNextRotaryPageCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.NextRotaryPage", "System.NextRotaryPage");
}

[Command("Device.PreviousRotaryPage", "Previous Rotary Page on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the previous rotary page on another device")]
public sealed class OtherDevicePreviousRotaryPageCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.PreviousRotaryPage", "System.PreviousRotaryPage");
}

[Command("Device.LastProfile", "Last Profile on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Switch another device to its last used profile")]
public sealed class OtherDeviceLastProfileCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.LastProfile", "System.LastProfile");
}

[Command("Device.NextWorkspace", "Next Workspace on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Switch another device to the next workspace of its active profile")]
public sealed class OtherDeviceNextWorkspaceCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.NextWorkspace", "System.NextWorkspace");
}

[Command("Device.PreviousWorkspace", "Previous Workspace on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Switch another device to the previous workspace of its active profile")]
public sealed class OtherDevicePreviousWorkspaceCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.PreviousWorkspace", "System.PreviousWorkspace");
}

[Command("Device.GoHomeWorkspace", "Home Workspace on Device", "Other Devices",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Return another device to the home workspace of its active profile")]
public sealed class OtherDeviceGoHomeWorkspaceCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        OtherDeviceCommandArgs.Run(devices, device, parameters, "Device.GoHomeWorkspace", "System.GoHomeWorkspace");
}

[Command("Device.ActivateProfile", "Activate Profile on Device", "Other Devices",
    parameterTemplate: "({Device},{Profile})",
    parameterNames: ["Device", "Profile"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Activate a profile on another device and open its home workspace")]
public sealed class OtherDeviceActivateProfileCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (!OtherDeviceCommandArgs.TryParseId(parameters, "Device.ActivateProfile", "profileId", out string target, out Guid profile))
            return Task.CompletedTask;

        return OtherDeviceCommandArgs.RunOn(devices, device, target, async (_, commands) =>
            await commands.ExecuteCommand($"System.ActivateProfile({profile})", ButtonTargets.None));
    }
}

[Command("Device.GotoWorkspace", "Go to Workspace on Device", "Other Devices",
    parameterTemplate: "({Device},{Workspace})",
    parameterNames: ["Device", "Workspace"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Switch another device to a workspace, activating its profile first when needed")]
public sealed class OtherDeviceGotoWorkspaceCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (!OtherDeviceCommandArgs.TryParseId(parameters, "Device.GotoWorkspace", "workspaceId", out string target, out Guid workspace))
            return Task.CompletedTask;

        return OtherDeviceCommandArgs.RunOn(devices, device, target, async (config, commands) =>
        {
            Profile owner = OtherDeviceCommandArgs.ProfileOf(config, workspace);
            if (owner == null)
            {
                Console.WriteLine($"[Devices] Workspace {workspace} not found on '{target}'.");
                return;
            }

            await OtherDeviceCommandArgs.EnsureProfile(config, commands, owner);
            await commands.ExecuteCommand($"System.GotoWorkspace({workspace})", ButtonTargets.None);
        });
    }
}

[Command("Device.GotoTouchPage", "Open Touch Page on Device", "Other Devices",
    parameterTemplate: "({Device},{Page})",
    parameterNames: ["Device", "Page"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Open a touch page on another device, switching to its profile and workspace first when needed")]
public sealed class OtherDeviceGotoTouchPageCommand(ICompanionCoordinator devices, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (!OtherDeviceCommandArgs.TryParseId(parameters, "Device.GotoTouchPage", "pageId", out string target, out Guid page))
            return Task.CompletedTask;

        return OtherDeviceCommandArgs.RunOn(devices, device, target, async (config, commands) =>
        {
            (Profile owner, Workspace workspace, int index) = OtherDeviceCommandArgs.TouchPageOf(config, page);
            if (workspace == null)
            {
                Console.WriteLine($"[Devices] Touch page {page} not found on '{target}'.");
                return;
            }

            await OtherDeviceCommandArgs.EnsureProfile(config, commands, owner);
            if (config.ActiveWorkspaceId != workspace.Id)
                await commands.ExecuteCommand($"System.GotoWorkspace({workspace.Id})", ButtonTargets.None);

            // A companion that follows its master keeps the master's workspace: the page is only
            // opened when the device really shows the page's workspace now.
            if (config.ActiveWorkspaceId == workspace.Id)
                await commands.ExecuteCommand($"System.GotoPage({index + 1})", ButtonTargets.None);
        });
    }
}

/// <summary>Parameter parsing and dispatch shared by the other-device commands.</summary>
internal static class OtherDeviceCommandArgs
{
    /// <summary>Runs <paramref name="command"/> on the device named by the only parameter.</summary>
    public static Task Run(ICompanionCoordinator devices, ResolvedDevice caller, string[] parameters,
        string commandName, string command)
    {
        string target = parameters is { Length: 1 } ? parameters[0].Trim() : null;
        if (string.IsNullOrEmpty(target))
        {
            Console.WriteLine($"Usage: {commandName}(deviceKey)");
            return Task.CompletedTask;
        }

        return RunOn(devices, caller, target, (_, commands) => commands.ExecuteCommand(command, ButtonTargets.None));
    }

    public static bool TryParseId(string[] parameters, string commandName, string idName, out string target, out Guid id)
    {
        target = parameters is { Length: 2 } ? parameters[0].Trim() : null;
        id = Guid.Empty;
        if (!string.IsNullOrEmpty(target) && Guid.TryParse(parameters[1], out id))
            return true;

        Console.WriteLine($"Usage: {commandName}(deviceKey, {idName})");
        return false;
    }

    /// <summary>
    /// Hands the running target device's config and command service to <paramref name="action"/> on
    /// the UI thread. Skipped when the target is not running; a device naming itself is skipped too,
    /// since the normal commands already cover it.
    /// </summary>
    public static Task RunOn(ICompanionCoordinator devices, ResolvedDevice caller, string targetKey,
        Func<LoupedeckConfig, ICommandService, Task> action)
    {
        if (string.Equals(caller?.ScopeKey, targetKey, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Devices] '{targetKey}' is this device - skipped.");
            return Task.CompletedTask;
        }

        DeviceHost host = devices.ResolveHost(targetKey);
        if (host == null)
        {
            Console.WriteLine($"[Devices] '{targetKey}' is not running - skipped.");
            return Task.CompletedTask;
        }

        LoupedeckConfig config = host.Provider.GetRequiredService<LoupedeckConfig>();
        ICommandService commands = host.Provider.GetRequiredService<ICommandService>();
        return Dispatcher.UIThread.InvokeAsync(() => action(config, commands));
    }

    /// <summary>Activates <paramref name="profile"/> on the target unless it is active already.</summary>
    public static async Task EnsureProfile(LoupedeckConfig config, ICommandService commands, Profile profile)
    {
        if (config.ActiveProfileId != profile.Id)
            await commands.ExecuteCommand($"System.ActivateProfile({profile.Id})", ButtonTargets.None);
    }

    public static Profile ProfileOf(LoupedeckConfig config, Guid workspaceId) =>
        config.Profiles?.FirstOrDefault(p => p.Workspaces?.Any(w => w.Id == workspaceId) == true);

    public static (Profile Profile, Workspace Workspace, int Index) TouchPageOf(LoupedeckConfig config, Guid pageId)
    {
        foreach (Profile profile in config.Profiles ?? [])
        foreach (Workspace workspace in profile.Workspaces ?? [])
        {
            int index = workspace.TouchButtonPages?.ToList().FindIndex(p => p.Id == pageId) ?? -1;
            if (index >= 0) return (profile, workspace, index);
        }

        return (null, null, -1);
    }
}
