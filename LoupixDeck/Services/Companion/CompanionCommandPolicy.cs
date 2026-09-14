namespace LoupixDeck.Services.Companion;

/// <summary>
/// Which commands a companion may not run itself. A companion follows its master's profile and
/// workspace, so every command that switches either is refused at execution time — for buttons,
/// dials, macros, CLI/IPC and plugins alike — and is not offered in the command picker. Paging
/// through the companion's own pages stays allowed. Existing assignments stay in the config and
/// work again once the device leaves its group.
/// </summary>
public static class CompanionCommandPolicy
{
    private static readonly HashSet<string> ContextSwitchCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.ActivateProfile",
        "System.GotoWorkspace",
        "System.NextWorkspace",
        "System.PreviousWorkspace",
        "System.GoHomeWorkspace"
    };

    /// <summary>True when <paramref name="commandName"/> switches the device's profile or workspace.</summary>
    public static bool IsContextSwitch(string commandName) =>
        !string.IsNullOrEmpty(commandName) && ContextSwitchCommands.Contains(commandName);

    /// <summary>True when the device with <paramref name="deviceKey"/> must not run
    /// <paramref name="commandName"/> because it is an active companion.</summary>
    public static bool IsBlocked(ICompanionCoordinator coordinator, string deviceKey, string commandName) =>
        IsContextSwitch(commandName) && coordinator.IsCompanion(deviceKey);
}
