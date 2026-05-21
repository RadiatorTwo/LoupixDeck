using LoupixDeck.Commands.Base;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Feeds the <see cref="ICommandRegistry"/> with commands contributed by loaded
/// plugins, adapting each <see cref="IPluginCommand"/> to a
/// <see cref="RegisteredCommand"/>.
/// </summary>
public class PluginCommandProvider : ICommandProvider
{
    private readonly IPluginManager _pluginManager;

    public PluginCommandProvider(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IEnumerable<RegisteredCommand> GetCommands()
    {
        var result = new List<RegisteredCommand>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (plugin.Status != PluginLoadStatus.Loaded)
                continue;

            foreach (var command in plugin.Commands)
            {
                try
                {
                    result.Add(Adapt(command, plugin.Host));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"PluginCommandProvider: '{plugin.Manifest?.Id}' command adapt failed: {ex.Message}");
                }
            }
        }

        return result;
    }

    private static RegisteredCommand Adapt(IPluginCommand command, IPluginHost host)
    {
        var descriptor = command.Descriptor;

        var info = new CommandInfo
        {
            CommandName = descriptor.CommandName,
            DisplayName = descriptor.DisplayName,
            Group = descriptor.Group,
            ParameterTemplate = descriptor.ParameterTemplate,
            Parameters = descriptor.Parameters
                .Select(p => new ParameterDescriptor(p.Name, p.ParameterType))
                .ToList()
        };

        Func<string[], Task> execute = parameters => command.Execute(new CommandContext
        {
            Parameters = parameters ?? Array.Empty<string>(),
            // The button that triggered execution is not tracked in the command
            // pipeline; plugins that need it can read it from GetText's context.
            Target = ButtonTargets.None,
            Device = host?.ActiveDevice,
            Host = host
        });

        var isDisplay = false;
        var interval = TimeSpan.Zero;
        Func<string[], string> getText = null;

        if (command is IDisplayCommand displayCommand)
        {
            isDisplay = true;
            interval = displayCommand.UpdateInterval;
            getText = parameters => displayCommand.GetText(new CommandContext
            {
                Parameters = parameters ?? Array.Empty<string>(),
                Target = ButtonTargets.TouchButton,
                Device = host?.ActiveDevice,
                Host = host
            });
        }

        return new RegisteredCommand
        {
            CommandName = descriptor.CommandName,
            Info = info,
            SupportedTargets = command.SupportedTargets,
            HiddenFromMenu = descriptor.HiddenFromMenu,
            IsDisplayCommand = isDisplay,
            UpdateInterval = interval,
            Execute = execute,
            GetText = getText
        };
    }
}
