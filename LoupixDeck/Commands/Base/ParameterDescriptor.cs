namespace LoupixDeck.Commands.Base;

public class ParameterDescriptor(string name, Type parameterType)
{
    public string Name { get; } = name;
    public Type ParameterType { get; } = parameterType;
}