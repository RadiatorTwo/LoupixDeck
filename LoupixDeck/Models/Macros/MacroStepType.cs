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
    WaitForCondition,
    Prompt
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

/// <summary>
/// Where a non-infinite <see cref="RepeatStartStep"/> gets its repeat count. Persisted; new
/// members must be appended (the enum name is written to macros.json).
/// </summary>
public enum RepeatCountMode
{
    /// <summary>A literal number entered in the editor (<see cref="RepeatStartStep.Count"/>).</summary>
    Fixed,

    /// <summary>The integer value of a macro variable (<see cref="RepeatStartStep.CountVariable"/>).</summary>
    Variable
}

/// <summary>
/// UI-only three-way selector for a <see cref="RepeatStartStep"/>. Maps onto the persisted
/// <see cref="RepeatStartStep.Infinite"/> + <see cref="RepeatStartStep.CountMode"/> fields and is
/// never serialized itself.
/// </summary>
public enum RepeatMode
{
    Fixed,
    Variable,
    Infinite
}

/// <summary>
/// How a <see cref="PromptStep"/> validates and stores the entered value. Default <see cref="Text"/>
/// reproduces the original free-text behaviour. Persisted; append new members only.
/// </summary>
public enum PromptInputType
{
    /// <summary>Any text (optionally length/regex restricted).</summary>
    Text,

    /// <summary>A whole number (decimals rejected).</summary>
    Integer,

    /// <summary>A number that may have a fractional part.</summary>
    Decimal,

    /// <summary>A yes/no value, stored as <c>"true"</c> or <c>"false"</c>.</summary>
    Boolean,

    /// <summary>One value picked from a fixed list.</summary>
    Selection
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