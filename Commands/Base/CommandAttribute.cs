namespace LoupixDeck.Commands.Base;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class CommandAttribute(string commandName) : Attribute
{
    public string CommandName { get; } = commandName;
}