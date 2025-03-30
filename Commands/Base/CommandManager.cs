using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Commands.Base;

public interface IExecutableCommand
{
    Task Execute(string[] parameters);
}

public static class CommandManager
{
    private static readonly Dictionary<string, Type> Commands = new();
    private static IServiceProvider _serviceProvider;
    
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IExecutableCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in commandTypes)
        {
            var attribute = type.GetCustomAttribute<CommandAttribute>();
            if (attribute != null)
            {
                Commands[attribute.CommandName] = type;
            }
        }
    }

    public static void ExecuteCommand(string commandName, string[] parameters)
    {
        if (Commands.TryGetValue(commandName, out Type type))
        {
            // ActivatorUtilities berücksichtigt den DI Container und löst alle Abhängigkeiten.
            var command = (IExecutableCommand)ActivatorUtilities.CreateInstance(_serviceProvider, type);
            command.Execute(parameters);
        }
        else
        {
            Console.WriteLine($"Command '{commandName}' wurde nicht gefunden.");
        }
    }

    public static bool CheckCommandExists(string commandName)
    {
        return Commands.ContainsKey(commandName);
    }
    
    public static IEnumerable<string> GetAvailableCommands() => Commands.Keys;
}