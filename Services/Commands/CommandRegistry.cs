using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <inheritdoc cref="ICommandRegistry"/>
public class CommandRegistry : ICommandRegistry
{
    private readonly IEnumerable<ICommandProvider> _providers;
    private readonly Dictionary<string, RegisteredCommand> _commands = new(StringComparer.Ordinal);

    public CommandRegistry(IEnumerable<ICommandProvider> providers)
    {
        _providers = providers;
    }

    public void Initialize()
    {
        _commands.Clear();

        foreach (var provider in _providers)
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

                _commands[command.CommandName] = command;
            }
        }
    }

    public bool Contains(string commandName)
    {
        return !string.IsNullOrEmpty(commandName) && _commands.ContainsKey(commandName);
    }

    public RegisteredCommand Get(string commandName)
    {
        if (commandName != null && _commands.TryGetValue(commandName, out var command))
            return command;

        return null;
    }

    public IEnumerable<RegisteredCommand> GetAll() => _commands.Values;

    public async Task Execute(string commandName, string[] parameters, ButtonTargets target)
    {
        var command = Get(commandName);
        if (command == null)
        {
            Console.WriteLine($"Command '{commandName}' not found.");
            return;
        }

        await command.Execute(parameters ?? Array.Empty<string>(), target);
    }
}
