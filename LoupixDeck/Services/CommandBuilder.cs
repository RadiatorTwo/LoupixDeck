using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services.Commands;
using System.Text;

namespace LoupixDeck.Services;

public interface ICommandBuilder
{
    string CreateCommandFromMenuEntry(MenuEntry menuEntry);

    /// <summary>
    /// The structured counterpart of <see cref="CreateCommandFromMenuEntry"/>, built through
    /// <see cref="BuildCall"/>; null when the entry's command is not registered.
    /// </summary>
    CommandCall CreateCallFromMenuEntry(MenuEntry menuEntry);
    string BuildCommandString(CommandInfo commandInfo, Dictionary<string, object> parameterValues);

    /// <summary>
    /// The structured counterpart of <see cref="BuildCommandString"/>: the same parameter list,
    /// but every value that fills a whole slot of the template arrives exactly as given.
    /// </summary>
    CommandCall BuildCall(CommandInfo commandInfo, Dictionary<string, object> parameterValues);
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

        return command == null ? string.Empty : BuildCommandString(command, ResolveMenuParameters(command, menuEntry));
    }

    public CommandCall CreateCallFromMenuEntry(MenuEntry menuEntry)
    {
        var command = _commandRegistry.Get(menuEntry.Command)?.Info;

        return command == null ? null : BuildCall(command, ResolveMenuParameters(command, menuEntry));
    }

    /// <summary>The value of every parameter of <paramref name="command"/> for a menu entry.</summary>
    private static Dictionary<string, object> ResolveMenuParameters(CommandInfo command, MenuEntry menuEntry)
    {
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
                    else if (IsCommandOwnEntry(command, menuEntry))
                    {
                        // The entry is the command itself, so its name is just the command's
                        // label. Pre-filling it would leave stock text in the value field that
                        // the user has to delete before typing (#585).
                        parameters.Add(parameter.Name, EmptyValue(parameter.ParameterType));
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

        return parameters;
    }

    /// <summary>
    /// True when the menu entry names the command itself (its display name or id) rather than a
    /// target such as a macro or plugin item.
    /// </summary>
    private static bool IsCommandOwnEntry(CommandInfo command, MenuEntry menuEntry) =>
        string.Equals(menuEntry.Name, command.DisplayName, StringComparison.Ordinal)
        || string.Equals(menuEntry.Name, command.CommandName, StringComparison.Ordinal);

    /// <summary>
    /// An empty text field for typed-in values; checkbox and combo parameters keep their type
    /// default so they still show a valid choice.
    /// </summary>
    private static object EmptyValue(Type type) =>
        type == typeof(bool) || type.IsEnum ? ParameterDefaults.GetDefaultValue(type) : string.Empty;

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

    /// <remarks>
    /// The template is split into its slots the way the executor reads a command string. A slot
    /// that is just one placeholder (<c>{Text}</c>) takes the value exactly, untrimmed and with
    /// any commas or parentheses it holds. A slot with literal text (<c>text=</c>, or a placeholder
    /// inside other text) gets its placeholders replaced, as <see cref="BuildCommandString"/>
    /// does. Trailing empty values are dropped, as the text form always dropped them, so commands
    /// that check the parameter count see what they did before.
    /// </remarks>
    public CommandCall BuildCall(CommandInfo commandInfo, Dictionary<string, object> parameterValues)
    {
        string[] slots = Utils.CommandStringParser.GetParameters("x" + commandInfo.ParameterTemplate);
        List<string> values = new(slots.Length);

        foreach (string slot in slots)
        {
            ParameterDescriptor whole = commandInfo.Parameters.FirstOrDefault(p => slot == "{" + p.Name + "}");
            if (whole != null)
            {
                values.Add(ValueOf(whole.Name));
                continue;
            }

            StringBuilder text = new(slot);
            foreach (ParameterDescriptor param in commandInfo.Parameters)
                text.Replace("{" + param.Name + "}", ValueOf(param.Name));
            values.Add(text.ToString());
        }

        int count = values.Count;
        while ((count > 0) && (values[count - 1].Length == 0))
            count--;

        return CommandCall.Create(commandInfo.CommandName, values.Take(count));

        string ValueOf(string name) =>
            parameterValues.TryGetValue(name, out object value) ? value?.ToString() ?? "null" : "null";
    }
}