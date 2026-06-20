namespace LoupixDeck.Services.Commands;

/// <summary>
/// Supplies <see cref="RegisteredCommand"/> entries to the
/// <see cref="ICommandRegistry"/>. The core registers one provider for built-in
/// <c>[Command]</c> classes; a second provider feeds in plugin commands.
/// </summary>
public interface ICommandProvider
{
    /// <summary>Returns all commands this provider currently exposes.</summary>
    IEnumerable<RegisteredCommand> GetCommands();
}
