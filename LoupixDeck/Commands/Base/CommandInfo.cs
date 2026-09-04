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

    /// <summary>
    /// The button states this command brings along, in order. Empty for commands that leave
    /// states to the user (every core command, and every plugin command that declares none).
    /// </summary>
    public List<CommandStateInfo> States { get; set; } = [];
}

/// <summary>One state a command declares, mirroring the SDK's ButtonStateDescriptor.</summary>
/// <param name="Name">State name, persisted on the button and used to address the state.</param>
/// <param name="Description">Optional one-line explanation shown in the editor.</param>
public sealed record CommandStateInfo(string Name, string Description);