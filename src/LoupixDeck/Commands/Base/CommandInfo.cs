namespace LoupixDeck.Commands.Base;

public class CommandInfo
{
    public string CommandName { get; set; }
    public string DisplayName { get; set; }
    public string Group { get; set; }
    public string ParameterTemplate { get; set; }
    public List<ParameterDescriptor> Parameters { get; set; } = [];
}