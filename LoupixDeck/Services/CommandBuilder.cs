using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services.Commands;
using System.Text;

namespace LoupixDeck.Services;

public interface ICommandBuilder
{
    string CreateCommandFromMenuEntry(MenuEntry menuEntry);
    string BuildCommandString(CommandInfo commandInfo, Dictionary<string, object> parameterValues);
}

public class CommandBuilder : ICommandBuilder
{
    private readonly ICommandRegistry _commandRegistry;

    public CommandBuilder(ICommandRegistry commandRegistry)
    {
        _commandRegistry = commandRegistry;
    }

    public string CreateCommandFromMenuEntry(MenuEntry menuEntry)
    {
        var command = _commandRegistry.Get(menuEntry.Command)?.Info;

        if (command == null) return string.Empty;

        var parameters = new Dictionary<string, object>();

        for (int i = 0; i < command.Parameters.Length; i++)
        {
            var parameter = command.Parameters[i];

            if (i == 0)
            {
                // First parameter is always Target.
                if (!string.IsNullOrEmpty(menuEntry.ParentName))
                {
                    parameters.Add(parameter.Name, menuEntry.ParentName);
                }
                else
                {
                    if (menuEntry.Parameters != null && menuEntry.Parameters.Count != 0)
                    {
                        var menuParameter = menuEntry.Parameters.First();
                        parameters.Add(menuParameter.Key, menuParameter.Value);
                    }
                    else
                    {
                        parameters.Add(parameter.Name, menuEntry.Name);
                    }
                }
            }
            else
            {
                parameters.Add(parameter.Name, ParameterDefaults.GetDefaultValue(parameter.ParameterType));
            }
        }

        return BuildCommandString(command, parameters);
    }

    public string BuildCommandString(CommandInfo commandInfo, Dictionary<string, object> parameterValues)
    {
        var sb = new StringBuilder();
        sb.Append(commandInfo.CommandName);

        var templateSb = new StringBuilder(commandInfo.ParameterTemplate);

        foreach (var param in commandInfo.Parameters)
        {
            var placeholder = "{" + param.Name + "}";
            var replacement = parameterValues.TryGetValue(param.Name, out var value)
                ? value?.ToString() ?? "null"
                : "null";
            templateSb.Replace(placeholder, replacement);
        }

        sb.Append(templateSb);
        return sb.ToString();
    }
}