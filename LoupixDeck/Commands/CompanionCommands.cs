using LoupixDeck.Commands.Base;
using LoupixDeck.Models.Companion;
using LoupixDeck.Registry;
using LoupixDeck.Services.Companion;

namespace LoupixDeck.Commands;

// Commands a master runs to page through one of its companions' pages (issue #232). They run on the
// master, whose scope key identifies the caller; the companion is named by its scope key and a page by
// its stable id in the companion's config. Profile and workspace stay shared, so a page outside the
// companion's active workspace is skipped. All are hidden: the picker offers them per companion through
// CompanionMenuContributor, and CompanionCommandPolicy refuses them on any device that is not a master.

[Command("Companion.GotoTouchPage", "Open Companion Touch Page", "Companions",
    parameterTemplate: "({Device},{Page})",
    parameterNames: ["Device", "Page"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Open a touch page on a companion")]
public sealed class CompanionGotoTouchPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.TryParsePage(parameters, "Companion.GotoTouchPage", out string companion, out Guid page)
            ? navigation.GotoPage(device.ScopeKey, new CompanionPageTarget { DeviceKey = companion, TouchPageId = page })
            : Task.CompletedTask;
}

[Command("Companion.GotoRotaryPage", "Open Companion Rotary Page", "Companions",
    parameterTemplate: "({Device},{Page})",
    parameterNames: ["Device", "Page"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Open a rotary page on a companion")]
public sealed class CompanionGotoRotaryPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.TryParsePage(parameters, "Companion.GotoRotaryPage", out string companion, out Guid page)
            ? navigation.GotoPage(device.ScopeKey, new CompanionPageTarget { DeviceKey = companion, RotaryPageId = page })
            : Task.CompletedTask;
}

[Command("Companion.GotoRotaryPageLeft", "Open Companion Left Rotary Page", "Companions",
    parameterTemplate: "({Device},{Page})",
    parameterNames: ["Device", "Page"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Open a left rotary page on a companion with side strips")]
public sealed class CompanionGotoLeftRotaryPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.TryParsePage(parameters, "Companion.GotoRotaryPageLeft", out string companion, out Guid page)
            ? navigation.GotoPage(device.ScopeKey, new CompanionPageTarget { DeviceKey = companion, LeftRotaryPageId = page })
            : Task.CompletedTask;
}

[Command("Companion.GotoRotaryPageRight", "Open Companion Right Rotary Page", "Companions",
    parameterTemplate: "({Device},{Page})",
    parameterNames: ["Device", "Page"],
    parameterTypes: [typeof(string), typeof(string)],
    Hidden = true,
    Description = "Open a right rotary page on a companion with side strips")]
public sealed class CompanionGotoRightRotaryPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.TryParsePage(parameters, "Companion.GotoRotaryPageRight", out string companion, out Guid page)
            ? navigation.GotoPage(device.ScopeKey, new CompanionPageTarget { DeviceKey = companion, RightRotaryPageId = page })
            : Task.CompletedTask;
}

[Command("Companion.NextTouchPage", "Next Companion Touch Page", "Companions",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the next touch page on a companion")]
public sealed class CompanionNextTouchPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.Step(navigation, device, parameters, "Companion.NextTouchPage", CompanionPageKind.Touch, next: true);
}

[Command("Companion.PreviousTouchPage", "Previous Companion Touch Page", "Companions",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the previous touch page on a companion")]
public sealed class CompanionPreviousTouchPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.Step(navigation, device, parameters, "Companion.PreviousTouchPage", CompanionPageKind.Touch, next: false);
}

[Command("Companion.NextRotaryPage", "Next Companion Rotary Page", "Companions",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the next rotary page on a companion")]
public sealed class CompanionNextRotaryPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.Step(navigation, device, parameters, "Companion.NextRotaryPage", CompanionPageKind.Rotary, next: true);
}

[Command("Companion.PreviousRotaryPage", "Previous Companion Rotary Page", "Companions",
    parameterTemplate: "({Device})",
    parameterNames: ["Device"],
    parameterTypes: [typeof(string)],
    Hidden = true,
    Description = "Go to the previous rotary page on a companion")]
public sealed class CompanionPreviousRotaryPageCommand(ICompanionNavigation navigation, ResolvedDevice device) : IExecutableCommand
{
    public Task Execute(string[] parameters) =>
        CompanionCommandArgs.Step(navigation, device, parameters, "Companion.PreviousRotaryPage", CompanionPageKind.Rotary, next: false);
}

/// <summary>Parameter parsing shared by the companion commands.</summary>
internal static class CompanionCommandArgs
{
    public static bool TryParsePage(string[] parameters, string commandName, out string companion, out Guid page)
    {
        companion = parameters is { Length: 2 } ? parameters[0].Trim() : null;
        page = Guid.Empty;
        if (!string.IsNullOrEmpty(companion) && Guid.TryParse(parameters[1], out page))
            return true;

        Console.WriteLine($"Usage: {commandName}(companionDeviceKey, pageId)");
        return false;
    }

    public static Task Step(ICompanionNavigation navigation, ResolvedDevice device, string[] parameters,
        string commandName, CompanionPageKind kind, bool next)
    {
        string companion = parameters is { Length: 1 } ? parameters[0].Trim() : null;
        if (string.IsNullOrEmpty(companion))
        {
            Console.WriteLine($"Usage: {commandName}(companionDeviceKey)");
            return Task.CompletedTask;
        }

        return navigation.StepPage(device.ScopeKey, companion, kind, next);
    }
}
