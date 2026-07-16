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
    Type[] parameterTypes = null,
    string[] parameterDefaults = null) : Attribute
{
    public string CommandName { get; } = commandName;
    public string DisplayName { get; } = displayName;
    public string Group { get; } = group;
    public string ParameterTemplate { get; set; } = parameterTemplate;

    /// <summary>
    /// Optional Material Design Icons glyph (a single UTF-32 code point as a string,
    /// e.g. <c>"\U000F040A"</c>) shown next to the command in the command picker.
    /// Null falls back to the category's icon. Purely cosmetic — never persisted.
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// Optional one-line description shown as the command's subtitle in the picker.
    /// Null shows no subtitle. Purely cosmetic — never persisted.
    /// </summary>
    public string Description { get; set; }

    public string[] ParameterNames { get; } = parameterNames;
    public Type[] ParameterTypes { get; } = parameterTypes;

    /// <summary>
    /// Optional per-parameter default values (aligned by index with
    /// <see cref="ParameterNames"/>/<see cref="ParameterTypes"/>). When a parameter has a
    /// default here, it pre-fills the command's settings flyout on insert instead of the
    /// generic target/type default. Absent or shorter than the parameter list simply means
    /// "no command-defined default" for the missing entries — fully backward compatible.
    /// </summary>
    public string[] ParameterDefaults { get; } = parameterDefaults;

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