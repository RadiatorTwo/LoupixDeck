namespace LoupixDeck.PluginSdk;

/// <summary>
/// The button types a command may be assigned to. A command declares its
/// supported targets explicitly via <see cref="IPluginCommand.SupportedTargets"/>;
/// the host filters the command-selection menu of each button type accordingly.
/// </summary>
[Flags]
public enum ButtonTargets
{
    None = 0,
    TouchButton = 1,
    SimpleButton = 2,
    RotaryEncoder = 4,
    All = TouchButton | SimpleButton | RotaryEncoder
}
