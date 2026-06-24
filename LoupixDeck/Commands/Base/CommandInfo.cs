namespace LoupixDeck.Commands.Base;

public class CommandInfo
{
    public required string CommandName { get; init; }
    public required string DisplayName { get; init; }
    public string Group { get; init; }
    public string ParameterTemplate { get; init; }
    public bool Hidden { get; init; }
    public List<ParameterDescriptor> Parameters { get; init; } = [];
}