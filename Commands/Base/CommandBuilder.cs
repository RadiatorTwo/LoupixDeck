using System.Text;

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
