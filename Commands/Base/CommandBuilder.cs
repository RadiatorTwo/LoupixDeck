using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoupixDeck.Commands.Base
{
    public static class CommandBuilder
    {
        public static string BuildCommandString(CommandInfo commandInfo, Dictionary<string, object> parameterValues)
        {
            var sb = new StringBuilder();
            sb.Append(commandInfo.CommandName);

            var templateSb = new StringBuilder(commandInfo.ParameterTemplate);

            foreach (var param in commandInfo.Parameters)
            {
                string placeholder = "{" + param.Name + "}";
                string replacement = parameterValues.TryGetValue(param.Name, out object value)
                    ? value.ToString()
                    : "null";
                templateSb.Replace(placeholder, replacement);
            }

            sb.Append(templateSb);
            return sb.ToString();
        }
    }
}
