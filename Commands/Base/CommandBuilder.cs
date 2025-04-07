﻿using LoupixDeck.Models;
using System.Text;

namespace LoupixDeck.Commands.Base
{
    public static class CommandBuilder
    {
        public static string CreateCommandFromMenuEntry(MenuEntry menuEntry)
        {
            var command = CommandManager.GetCommandInfo(menuEntry.Command);

            if (command == null) return string.Empty;

            var parameters = new Dictionary<string, object>();

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var parameter = command.Parameters[i];

                if (i == 0)
                {
                    // First parameter is always Target.
                    if (!string.IsNullOrEmpty(menuEntry.ParentName))
                    {
                        // When Parentname is not null, then that is the target.
                        parameters.Add(parameter.Name, menuEntry.ParentName);
                    }
                    else
                    {
                        parameters.Add(parameter.Name, menuEntry.Name);
                    }
                }
                else
                {
                    parameters.Add(parameter.Name, CommandManager.GetDefaultValue(parameter.ParameterType));
                }
            }

            return BuildCommandString(command, parameters);
        }

        public static string BuildCommandString(CommandInfo commandInfo, Dictionary<string, object> parameterValues)
        {
            var sb = new StringBuilder();
            sb.Append(commandInfo.CommandName);

            var templateSb = new StringBuilder(commandInfo.ParameterTemplate);

            foreach (var param in commandInfo.Parameters)
            {
                var placeholder = "{" + param.Name + "}";
                var replacement = parameterValues.TryGetValue(param.Name, out var value)
                    ? value.ToString()
                    : "null";
                templateSb.Replace(placeholder, replacement);
            }

            sb.Append(templateSb);
            return sb.ToString();
        }
    }
}
