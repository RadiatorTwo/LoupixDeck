namespace LoupixDeck.Models.Macros;

/// <summary>
/// Discriminator for the persisted macro step types. The enum NAME (not the
/// numeric value) is written to macros.json, so renaming a member is a breaking
/// change while appending new members is always safe.
/// </summary>
public enum MacroStepType
{
    Text,
    KeyCombination,
    Delay,
    KeyDown,
    KeyUp,
    Mouse,
    Command,
    RepeatStart,
    RepeatEnd,
    SetVariable,
    If,
    Else,
    EndIf,
    WaitForCondition
}

/// <summary>What a <see cref="WaitForConditionStep"/> does when its timeout elapses.</summary>
public enum WaitTimeoutBehavior
{
    /// <summary>Abort the rest of the macro.</summary>
    Fail,

    /// <summary>Carry on with the next step anyway.</summary>
    Continue
}

/// <summary>How a <see cref="SetVariableStep"/> changes its target variable.</summary>
public enum VariableOperation
{
    Set,
    Increment,
    Decrement
}

/// <summary>What a <see cref="MouseStep"/> does.</summary>
public enum MouseStepAction
{
    Click,
    Down,
    Up,
    MoveRelative,
    MoveAbsolute,
    Scroll
}

/// <summary>Mouse button used by click/down/up mouse steps.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle
}
