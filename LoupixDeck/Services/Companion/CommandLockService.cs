using LoupixDeck.Localization;
using LoupixDeck.Registry;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Tells the command editors of one device why an assigned command will not run there, so a
/// command its companion role refuses is shown as locked instead of silently doing nothing.
/// Per-device singleton.
/// </summary>
public interface ICommandLockService
{
    /// <summary>A user-facing reason when the single command <paramref name="command"/> (name plus
    /// parameters, e.g. <c>Companion.NextTouchPage(key)</c>) is locked on this device, else null.</summary>
    string GetLockHint(string command);
}

public sealed class CommandLockService(ICompanionCoordinator coordinator, ResolvedDevice device) : ICommandLockService
{
    public string GetLockHint(string command)
    {
        string name = CommandStringParser.GetName(command);

        if (CompanionCommandPolicy.IsContextSwitch(name) && coordinator.IsCompanion(device.ScopeKey))
            return Loc.Tr("Command_LockedOnCompanion");

        if (!CompanionCommandPolicy.IsCompanionCommand(name))
            return null;

        if (!coordinator.IsMaster(device.ScopeKey))
            return Loc.Tr("Command_CompanionCommandNeedsMaster");

        // The command runs, but the device it names left this master's group: it would do nothing.
        string[] parameters = CommandStringParser.GetParameters(command);
        string companionKey = parameters is { Length: > 0 } ? parameters[0].Trim() : null;
        return IsCompanionOfThisMaster(companionKey)
            ? null
            : Loc.Tr("Command_CompanionNotInGroup", coordinator.GetDisplayName(companionKey));
    }

    private bool IsCompanionOfThisMaster(string companionKey)
    {
        if (string.IsNullOrWhiteSpace(companionKey)) return false;
        if (coordinator.IsCompanionOf(device.ScopeKey, companionKey)) return true;

        // A slug-only key stored before the group learnt the serial still names its companion.
        return !CompanionGroupValidator.HasSerial(companionKey) &&
               coordinator.GetCompanionKeys(device.ScopeKey)
                   .Count(key => key.StartsWith(companionKey + "_", StringComparison.OrdinalIgnoreCase)) == 1;
    }
}
