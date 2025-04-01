using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Commands.Base;

public interface IExecutableCommand
{
    Task Execute(string[] parameters);
}

public static class CommandManager
{
    private static readonly Dictionary<string, (Type CommandType, CommandAttribute Attribute)> Commands = new();
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
                Commands[attribute.CommandName] = (type, attribute);
            }
        }
    }

    public static void ExecuteCommand(string commandName, string[] parameters)
    {
        if (Commands.TryGetValue(commandName, out var command))
        {
            var executableCommand =
                (IExecutableCommand)ActivatorUtilities.CreateInstance(_serviceProvider, command.CommandType);
            executableCommand.Execute(parameters);
        }
        else
        {
            Console.WriteLine($"Command '{commandName}' not found.");
        }
    }

    public static bool CheckCommandExists(string commandName)
    {
        return Commands.ContainsKey(commandName);
    }

    public static CommandInfo GetCommandInfo(string commandName)
    {
        if (Commands.TryGetValue(commandName, out var entry))
        {
            return new CommandInfo
            {
                CommandName = commandName,
                DisplayName = entry.Attribute.DisplayName,
                Group = entry.Attribute.Group,
                ParameterTemplate = entry.Attribute.ParameterTemplate,
                Parameters = CreateParameterDescriptors(entry.Attribute)
            };
        }
        
        return null;
    }

    public static IEnumerable<CommandInfo> GetCommandInfos()
    {
        return Commands.Select(kvp => new CommandInfo
        {
            CommandName = kvp.Key,
            DisplayName = kvp.Value.Attribute.DisplayName,
            Group = kvp.Value.Attribute.Group,
            ParameterTemplate = kvp.Value.Attribute.ParameterTemplate,
            Parameters = CreateParameterDescriptors(kvp.Value.Attribute)
        });
    }
    
    private static List<ParameterDescriptor> CreateParameterDescriptors(CommandAttribute attribute)
    {
        var list = new List<ParameterDescriptor>();
        
        if (attribute.ParameterNames == null || attribute.ParameterTypes == null) return list;
      
        for (var i = 0; i < attribute.ParameterNames.Length; i++)
        {
            list.Add(new ParameterDescriptor(
                attribute.ParameterNames[i],
                attribute.ParameterTypes[i]
            ));
        }
        
        return list;
    }

    public static object GetDefaultValue(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        if (type == typeof(string))
            return "string";

        if (type == typeof(bool))
            return false;
        if (type == typeof(char))
            return '\0';
        if (type == typeof(byte))
            return (byte)0;
        if (type == typeof(sbyte))
            return (sbyte)0;
        if (type == typeof(short))
            return (short)0;
        if (type == typeof(ushort))
            return (ushort)0;
        if (type == typeof(int))
            return 0;
        if (type == typeof(uint))
            return 0U;
        if (type == typeof(long))
            return 0L;
        if (type == typeof(ulong))
            return 0UL;
        if (type == typeof(float))
            return 0f;
        if (type == typeof(double))
            return 0.0;
        if (type == typeof(decimal))
            return 0m;

        if (type == typeof(DateTime))
            return default(DateTime);
        if (type == typeof(Guid))
            return Guid.Empty;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            if (values.Length > 0)
                return values.GetValue(0);
            else
                return Activator.CreateInstance(type);
        }

        if (Nullable.GetUnderlyingType(type) != null)
            return null;

        if (type.IsValueType)
            return Activator.CreateInstance(type);

        return null;
    }
}