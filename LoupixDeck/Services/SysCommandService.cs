using System.Collections.Immutable;
using System.Reflection;
using LoupixDeck.Commands.Base;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services;

public interface ISysCommandService
{
    /// <summary>
    /// Initializes the available commands. 
    /// </summary>
    void Initialize();

    /// <summary>
    /// Executes the specified command.
    /// </summary>
    Task ExecuteCommand(string commandName, string[] parameters);

    /// <summary>
    /// Checks whether a command exists.
    /// </summary>
    bool CheckCommandExists(string commandName);

    /// <summary>
    /// Returns information about a command.
    /// </summary>
    CommandInfo GetCommandInfo(string commandName);

    /// <summary>
    /// Returns information about all available commands.
    /// </summary>
    IEnumerable<CommandInfo> GetCommandInfos();

    /// <summary>
    /// Returns the default value for a given type
    /// </summary>
    object GetDefaultValue(Type type);

    /// <summary>
    /// Looks up the implementation type for a registered command name.
    /// </summary>
    bool TryGetCommandType(string commandName, out Type type);
}

public class SysCommandService(IServiceProvider serviceProvider, IUInputKeyboard uInputKeyboard) : ISysCommandService
{
    private ImmutableDictionary<string, (Type CommandType, CommandAttribute Attribute)> commands = ImmutableDictionary<string, (Type CommandType, CommandAttribute Attribute)>.Empty;

    /// <summary>
    /// Initializes the available commands by scanning the assembly.
    /// </summary>
    public void Initialize()
    {
        var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IExecutableCommand).IsAssignableFrom(t)
                        && !t.IsInterface
                        && !t.IsAbstract);

        foreach (var type in commandTypes)
        {
            var attribute = type.GetCustomAttribute<CommandAttribute>();
            if (attribute == null)
                continue;

            if (attribute.Platform == CommandPlatform.Windows && !OperatingSystem.IsWindows())
                continue;
            if (attribute.Platform == CommandPlatform.Linux && !OperatingSystem.IsLinux())
                continue;

            commands = commands.SetItem(attribute.CommandName, (type, attribute));
        }
    }

    public async Task ExecuteCommand(string commandName, string[] parameters)
    {
        if (commands.TryGetValue(commandName, out var command))
        {
            var executableCommand =
                (IExecutableCommand)ActivatorUtilities.CreateInstance(serviceProvider, command.CommandType);
            await executableCommand.Execute(parameters);
        }
        else
        {
            Console.WriteLine($"Command '{commandName}' not found.");
        }
    }

    public bool CheckCommandExists(string commandName)
    {
        return commands.ContainsKey(commandName);
    }

    public bool TryGetCommandType(string commandName, out Type type)
    {
        if (commands.TryGetValue(commandName, out var entry))
        {
            type = entry.CommandType;
            return true;
        }

        type = null;
        return false;
    }

    public CommandInfo GetCommandInfo(string commandName)
    {
        if (commands.TryGetValue(commandName, out var entry))
        {
            return new CommandInfo
            {
                CommandName = commandName,
                DisplayName = entry.Attribute.DisplayName,
                Group = entry.Attribute.Group,
                ParameterTemplate = entry.Attribute.ParameterTemplate,
                Hidden = entry.Attribute.Hidden,
                Parameters = CreateParameterDescriptors(entry.Attribute)
            };
        }

        return null;
    }

    public IEnumerable<CommandInfo> GetCommandInfos()
    {
        return commands.Select(kvp => new CommandInfo
        {
            CommandName = kvp.Key,
            DisplayName = kvp.Value.Attribute.DisplayName,
            Group = kvp.Value.Attribute.Group,
            ParameterTemplate = kvp.Value.Attribute.ParameterTemplate,
            Hidden = kvp.Value.Attribute.Hidden,
            Parameters = CreateParameterDescriptors(kvp.Value.Attribute)
        });
    }

    public object GetDefaultValue(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return false;
        if (type == typeof(char)) return '\0';
        if (type == typeof(byte)) return (byte)0;
        if (type == typeof(sbyte)) return (sbyte)0;
        if (type == typeof(short)) return (short)0;
        if (type == typeof(ushort)) return (ushort)0;
        if (type == typeof(int)) return 0;
        if (type == typeof(uint)) return 0U;
        if (type == typeof(long)) return 0L;
        if (type == typeof(ulong)) return 0UL;
        if (type == typeof(float)) return 0f;
        if (type == typeof(double)) return 0.0;
        if (type == typeof(decimal)) return 0m;
        if (type == typeof(DateTime)) return default(DateTime);
        if (type == typeof(Guid)) return Guid.Empty;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0
                ? values.GetValue(0)
                : Activator.CreateInstance(type);
        }

        if (Nullable.GetUnderlyingType(type) != null)
            return null;

        if (type.IsValueType)
            return Activator.CreateInstance(type);

        return null;
    }

    private List<ParameterDescriptor> CreateParameterDescriptors(CommandAttribute attribute)
    {
        var list = new List<ParameterDescriptor>();

        if (attribute.ParameterNames == null || attribute.ParameterTypes == null)
            return list;

        for (var i = 0; i < attribute.ParameterNames.Length; i++)
        {
            list.Add(new ParameterDescriptor(
                attribute.ParameterNames[i],
                attribute.ParameterTypes[i]
            ));
        }

        return list;
    }
}
