using LoupixDeck.Commands.Base;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace LoupixDeck.Services
{
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
    }

    public class SysCommandService : ISysCommandService
    {
        private readonly Dictionary<string, (Type CommandType, CommandAttribute Attribute)> _commands
            = new Dictionary<string, (Type CommandType, CommandAttribute Attribute)>();

        private readonly IServiceProvider _serviceProvider;
        private readonly IUInputKeyboard _uInputKeyboard;

        public SysCommandService(IServiceProvider serviceProvider, IUInputKeyboard uInputKeyboard)
        {
            _serviceProvider = serviceProvider;
            _uInputKeyboard = uInputKeyboard;
        }

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
                if (attribute != null)
                {
                    _commands[attribute.CommandName] = (type, attribute);
                }
            }
        }

        public async Task ExecuteCommand(string commandName, string[] parameters)
        {
            if (_commands.TryGetValue(commandName, out var command))
            {
                var executableCommand =
                    (IExecutableCommand)ActivatorUtilities.CreateInstance(_serviceProvider, command.CommandType);
                await executableCommand.Execute(parameters);
            }
            else
            {
                Console.WriteLine($"Command '{commandName}' not found.");
            }
        }

        public bool CheckCommandExists(string commandName)
        {
            return _commands.ContainsKey(commandName);
        }

        public CommandInfo GetCommandInfo(string commandName)
        {
            if (_commands.TryGetValue(commandName, out var entry))
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

        public IEnumerable<CommandInfo> GetCommandInfos()
        {
            return _commands.Select(kvp => new CommandInfo
            {
                CommandName = kvp.Key,
                DisplayName = kvp.Value.Attribute.DisplayName,
                Group = kvp.Value.Attribute.Group,
                ParameterTemplate = kvp.Value.Attribute.ParameterTemplate,
                Parameters = CreateParameterDescriptors(kvp.Value.Attribute)
            });
        }

        public object GetDefaultValue(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

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
}
