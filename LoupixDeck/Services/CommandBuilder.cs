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
        Dictionary<string, string> named = MatchMenuParameters(command, menuEntry);

        for (int i = 0; i < command.Parameters.Count; i++)
        {
            var parameter = command.Parameters[i];

            // A menu value addressed to this parameter by name wins. Otherwise a command-defined
            // default pre-fills the settings flyout with the value the command declares (e.g. a
            // rotary adjustment's step). Only when neither exists do we fall back to the legacy
            // behaviour: the first parameter is treated as the menu-derived Target, the rest get
            // a type default.
            if (named.TryGetValue(parameter.Name, out string namedValue))
            {
                parameters.Add(parameter.Name, namedValue);
            }
            else if (!string.IsNullOrEmpty(parameter.DefaultValue))
            {
                parameters.Add(parameter.Name, parameter.DefaultValue);
            }
            else if (i == 0 && named.Count == 0)
            {
                // Legacy style, only when no menu key matched a parameter name — once one did,
                // matching is purely by name so a by-name value never leaks into Target.
                // First parameter is always Target.
                if (!string.IsNullOrEmpty(menuEntry.ParentName))
                {
                    parameters.Add(parameter.Name, menuEntry.ParentName);
                }
                else
                {
                    // A single value under an arbitrary key. It is stored under the parameter's
                    // own name; stored under its key it would never reach the template.
                    if (menuEntry.Parameters != null && menuEntry.Parameters.Count != 0)
                    {
                        parameters.Add(parameter.Name, menuEntry.Parameters.First().Value);
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

    /// <summary>
    /// Maps the menu entry's parameter values onto the command's parameter names,
    /// case-insensitively. The result is keyed by the declared parameter name; keys that
    /// match no parameter are dropped and logged, unless none matched at all — that is the
    /// legacy single-value style, whose key is arbitrary by design.
    /// </summary>
    private static Dictionary<string, string> MatchMenuParameters(CommandInfo command, MenuEntry menuEntry)
    {
        Dictionary<string, string> named = new(StringComparer.OrdinalIgnoreCase);
        if (menuEntry.Parameters == null || menuEntry.Parameters.Count == 0)
            return named;

        Dictionary<string, string> declared = new(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in command.Parameters)
            declared.TryAdd(parameter.Name, parameter.Name);

        List<string> unmatched = [];
        foreach ((string key, string value) in menuEntry.Parameters)
        {
            if (key != null && declared.TryGetValue(key, out string name))
                named[name] = value;
            else
                unmatched.Add(key);
        }

        if (named.Count > 0 && unmatched.Count > 0)
        {
            Console.WriteLine(
                $"CommandBuilder: menu entry '{menuEntry.Name}' passes unknown parameter(s) " +
                $"{string.Join(", ", unmatched)} to '{command.CommandName}'; ignored.");
        }

        return named;
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