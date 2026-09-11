namespace LoupixDeck.Commands.Base;

/// <summary>
/// Ids of the dialogs that can fill a command parameter instead of the user typing its value,
/// used by <see cref="CommandAttribute.ParameterPickers"/> and rendered as a small button next to
/// the parameter's text box.
/// </summary>
public static class ParameterPicker
{
    /// <summary>Record one key combination, e.g. "Ctrl+Shift+S".</summary>
    public const string KeyCombination = "KeyCombination";

    /// <summary>Record several key combinations that replay one after another.</summary>
    public const string KeySequence = "KeySequence";

    /// <summary>Record modifier keys only, e.g. "Ctrl+Shift".</summary>
    public const string Modifiers = "Modifiers";
}
