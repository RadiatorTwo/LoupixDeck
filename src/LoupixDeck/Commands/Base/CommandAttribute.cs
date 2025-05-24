namespace LoupixDeck.Commands.Base;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class CommandAttribute(
    string commandName,
    string displayName,
    string group,
    string parameterTemplate = null,
    string[] parameterNames = null,
    Type[] parameterTypes = null) : Attribute
{
    public string CommandName { get; } = commandName;
    public string DisplayName { get; } = displayName;
    public string Group { get; } = group;
    public string ParameterTemplate { get; set; } = parameterTemplate;

    public string[] ParameterNames { get; } = parameterNames;
    public Type[] ParameterTypes { get; } =  parameterTypes;
}