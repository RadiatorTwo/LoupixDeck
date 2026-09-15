namespace LoupixDeck.Services.Companion;

/// <summary>
/// Which commands a device may not run because of its companion role, checked at execution time —
/// for buttons, dials, macros, CLI/IPC and plugins alike — and in the command picker. Existing
/// assignments stay in the config and work again once the role allows them.
/// <list type="bullet">
/// <item>A companion follows its master's profile and workspace, so every command that switches
/// either is refused there while its group is not paused. Paging through the companion's own pages
/// stays allowed.</item>
/// <item>Companion commands (<c>Companion.*</c>) page a master's companions, so only a master runs them.</item>
/// </list>
/// </summary>
public static class CompanionCommandPolicy
{
    private const string CompanionCommandPrefix = "Companion.";

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

    /// <summary>True when <paramref name="commandName"/> drives one of the device's companions.</summary>
    public static bool IsCompanionCommand(string commandName) =>
        !string.IsNullOrEmpty(commandName) &&
        commandName.StartsWith(CompanionCommandPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the device with <paramref name="deviceKey"/> must not run
    /// <paramref name="commandName"/>: a context switch on a companion that follows its master, or a
    /// companion command on a device that is not an active master.</summary>
    public static bool IsBlocked(ICompanionCoordinator coordinator, string deviceKey, string commandName) =>
        (IsContextSwitch(commandName) && coordinator.IsFollowingMaster(deviceKey)) ||
        (IsCompanionCommand(commandName) && !coordinator.IsMaster(deviceKey));
}
