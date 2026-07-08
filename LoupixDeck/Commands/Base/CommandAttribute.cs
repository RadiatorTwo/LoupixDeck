namespace LoupixDeck.Commands.Base;

public enum CommandPlatform
{
    All,
    Windows,
    Linux
}

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

    public CommandPlatform Platform { get; set; } = CommandPlatform.All;

    /// <summary>
    /// When true the command is registered and remains executable (a button can
    /// still be assigned <c>Name(args)</c> manually, and the pipeline runs it),
    /// but it is not listed in the command-selection menu. Use for internal /
    /// developer commands that should not be user-discoverable.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// When true the command is only listed in the command-selection menu for devices
    /// that have separate side-display rotary areas (<c>HasSideStrips</c>: Loupedeck Live,
    /// Razer Stream Controller, Loupedeck CT). Devices without them (e.g. the Loupedeck
    /// Live S) never see it. Like <see cref="Hidden"/>, this only affects menu listing —
    /// the command stays registered and executable by name.
    /// </summary>
    public bool RequiresSideStrips { get; set; }
}
