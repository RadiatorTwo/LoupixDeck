using LoupixDeck.Localization;
using LoupixDeck.Registry;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Tells the command editors of one device why an assigned command will not run there, so a
/// profile or workspace switch kept on a companion is shown as locked instead of silently doing nothing.
/// Per-device singleton.
/// </summary>
public interface ICommandLockService
{
    /// <summary>A user-facing reason when <paramref name="commandName"/> is locked on this device, else null.</summary>
    string GetLockHint(string commandName);
}

public sealed class CommandLockService(ICompanionCoordinator coordinator, ResolvedDevice device) : ICommandLockService
{
    public string GetLockHint(string commandName) =>
        CompanionCommandPolicy.IsBlocked(coordinator, device.ScopeKey, commandName)
            ? Loc.Tr("Command_LockedOnCompanion")
            : null;
}
