namespace LoupixDeck.Commands.Base;

public class ParameterDescriptor(
    string name,
    Type parameterType,
    string defaultValue = null,
    string picker = null)
{
    public string Name { get; } = name;
    public Type ParameterType { get; } = parameterType;

    /// <summary>
    /// Optional command-defined default for this parameter, used to pre-fill the editor's
    /// per-command settings flyout when the command is inserted into a sequence. Null means
    /// no command-defined default (the builder falls back to the menu-derived target for the
    /// first parameter, or a type default for the rest).
    /// </summary>
    public string DefaultValue { get; } = defaultValue;

    /// <summary>
    /// Optional id of a dialog that fills this parameter instead of the user typing it (see
    /// <see cref="ParameterPicker"/>). Null — the normal case, and what every plugin command
    /// gets — means the editor shows a plain text box.
    /// </summary>
    public string Picker { get; } = picker;
}