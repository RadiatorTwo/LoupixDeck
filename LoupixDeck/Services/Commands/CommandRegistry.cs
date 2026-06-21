using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <inheritdoc cref="ICommandRegistry"/>
public class CommandRegistry(IEnumerable<ICommandProvider> providers) : ICommandRegistry
{
    // Immutable, copy-on-write map. Initialize() builds a fresh dictionary and
    // publishes it via a single volatile reference swap, so a runtime rebuild
    // (plugin hot-reload) can never tear a read on a device input thread —
    // readers take a local copy of the reference and an in-flight Execute keeps
    // running against the snapshot it already captured.
    private volatile IReadOnlyDictionary<string, RegisteredCommand> _commands =
        new Dictionary<string, RegisteredCommand>(StringComparer.Ordinal);

    public void Initialize()
    {
        var next = new Dictionary<string, RegisteredCommand>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            List<RegisteredCommand> commands;
            try
            {
                commands = provider.GetCommands().ToList();
            }
            catch (Exception ex)
            {
                // A faulty provider (e.g. a misbehaving plugin) must not take
                // down the whole registry.
                Console.WriteLine($"CommandRegistry: provider '{provider.GetType().Name}' failed: {ex.Message}");
                continue;
            }

            foreach (var command in commands)
            {
                if (command == null || string.IsNullOrEmpty(command.CommandName))
                    continue;

                next[command.CommandName] = command;
            }
        }

        _commands = next; // atomic publish
    }

    public bool Contains(string commandName)
    {
        return !string.IsNullOrEmpty(commandName) && _commands.ContainsKey(commandName);
    }

    public RegisteredCommand Get(string commandName)
    {
        var map = _commands; // local copy of the reference — never torn
        if (commandName != null && map.TryGetValue(commandName, out var command))
            return command;

        return null;
    }

    public IEnumerable<RegisteredCommand> GetAll() => _commands.Values;

    public async Task Execute(string commandName, string[] parameters, ButtonTargets target, int? sourceIndex = null)
    {
        var command = Get(commandName);
        if (command == null)
        {
            Console.WriteLine($"Command '{commandName}' not found.");
            return;
        }

        await command.Execute(parameters ?? Array.Empty<string>(), target, sourceIndex);
    }
}
