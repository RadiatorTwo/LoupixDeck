namespace LoupixDeck.Commands.Base;

public class CommandInfo
{
    public string CommandName { get; set; }
    public string DisplayName { get; set; }
    public string Group { get; set; }

    /// <summary>Optional MDI glyph shown next to the command in the picker; null falls back to the category icon.</summary>
    public string Icon { get; set; }

    /// <summary>Optional one-line subtitle shown under the command title in the picker.</summary>
    public string Description { get; set; }

    public string ParameterTemplate { get; set; }
    public bool Hidden { get; set; }
    public bool RequiresSideStrips { get; set; }
    public List<ParameterDescriptor> Parameters { get; set; } = [];
}