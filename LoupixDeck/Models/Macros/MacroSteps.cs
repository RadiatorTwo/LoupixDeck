using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models.Macros;

/// <summary>Types a text string via the virtual keyboard.</summary>
public class TextStep : MacroStep
{
    public string Text
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.Text;
    public override string Icon => Glyph(0xF030C); // mdi-keyboard
    public override string TypeText => "Type Text";
    public override string ValueText => Text ?? string.Empty;
}

/// <summary>Presses a key combination, e.g. "Ctrl+Shift+Esc".</summary>
public class KeyCombinationStep : MacroStep
{
    /// <summary>Key names joined with '+', same syntax as System.KeyCombination.</summary>
    public string Keys
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.KeyCombination;
    public override string Icon => Glyph(0xF0317); // mdi-keyboard-variant
    public override string TypeText => "Key Combination";
    public override string ValueText => Keys ?? string.Empty;
}

/// <summary>Waits for a fixed amount of time before the next step.</summary>
public class DelayStep : MacroStep
{
    public int Milliseconds
    {
        get;
        set => SetValueProperty(ref field, value);
    } = 100;

    public override MacroStepType StepType => MacroStepType.Delay;
    public override string Icon => Glyph(0xF051F); // mdi-timer-sand
    public override string TypeText => "Delay";
    public override string ValueText => $"{Milliseconds} ms";
}

/// <summary>Presses (and holds) a single key without releasing it.</summary>
public class KeyDownStep : MacroStep
{
    public string Key
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.KeyDown;
    public override string Icon => Glyph(0xF013C); // mdi-chevron-double-down
    public override string TypeText => "Key Down";
    public override string ValueText => Key ?? string.Empty;
}

/// <summary>Releases a key previously held down by a <see cref="KeyDownStep"/>.</summary>
public class KeyUpStep : MacroStep
{
    public string Key
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.KeyUp;
    public override string Icon => Glyph(0xF013F); // mdi-chevron-double-up
    public override string TypeText => "Key Up";
    public override string ValueText => Key ?? string.Empty;
}

/// <summary>Performs a mouse action (click, button down/up, move, scroll).</summary>
public partial class MouseStep : MacroStep
{
    /// <summary>All selectable actions/buttons — bound by the editor's ComboBoxes.</summary>
    public static MouseStepAction[] AllActions { get; } = Enum.GetValues<MouseStepAction>();

    public static MouseButton[] AllButtons { get; } = Enum.GetValues<MouseButton>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsButton), nameof(ShowsCoordinates))]
    [NotifyPropertyChangedFor(nameof(ShowsAmount), nameof(ShowsAbsoluteHint))]
    public partial MouseStepAction Action { get; set; } = MouseStepAction.Click;

    /// <summary>Editor visibility helpers — which fields apply to the selected action.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool ShowsButton => Action is MouseStepAction.Click or MouseStepAction.Down or MouseStepAction.Up;

    [Newtonsoft.Json.JsonIgnore]
    public bool ShowsCoordinates => Action is MouseStepAction.MoveRelative or MouseStepAction.MoveAbsolute;

    [Newtonsoft.Json.JsonIgnore]
    public bool ShowsAmount => Action == MouseStepAction.Scroll;

    [Newtonsoft.Json.JsonIgnore]
    public bool ShowsAbsoluteHint => Action == MouseStepAction.MoveAbsolute;

    public MouseButton Button
    {
        get;
        set => SetValueProperty(ref field, value);
    } = MouseButton.Left;

    /// <summary>X coordinate: relative delta for MoveRelative, screen pixel for MoveAbsolute.</summary>
    public int X
    {
        get;
        set => SetValueProperty(ref field, value);
    }

    /// <summary>Y coordinate: relative delta for MoveRelative, screen pixel for MoveAbsolute.</summary>
    public int Y
    {
        get;
        set => SetValueProperty(ref field, value);
    }

    /// <summary>Scroll amount in wheel detents (positive = up, negative = down).</summary>
    public int Amount
    {
        get;
        set => SetValueProperty(ref field, value);
    } = 1;

    public override MacroStepType StepType => MacroStepType.Mouse;
    public override string Icon => Glyph(0xF037D); // mdi-mouse
    public override string TypeText => "Mouse";

    public override string ValueText => Action switch
    {
        MouseStepAction.Click => $"{Button} Click",
        MouseStepAction.Down => $"{Button} Down",
        MouseStepAction.Up => $"{Button} Up",
        MouseStepAction.MoveRelative => $"Move by {X}, {Y}",
        MouseStepAction.MoveAbsolute => $"Move to {X}, {Y}",
        MouseStepAction.Scroll => $"Scroll {Amount}",
        _ => string.Empty
    };
}

/// <summary>
/// Marks the start of a repeated block. Every step up to the matching
/// <see cref="RepeatEndStep"/> runs <see cref="Count"/> times, with an optional
/// delay between iterations. Markers are matched by order (nesting supported);
/// an unmatched start simply runs its body to the end of the macro once.
/// </summary>
public class RepeatStartStep : MacroStep
{
    /// <summary>Number of times the block runs (clamped to at least 1 at execution time).</summary>
    public int Count
    {
        get;
        set => SetValueProperty(ref field, value);
    } = 2;

    /// <summary>Optional pause inserted between iterations (not after the last one).</summary>
    public int LoopDelayMilliseconds
    {
        get;
        set => SetValueProperty(ref field, value);
    }

    /// <summary>
    /// When true the block repeats forever (until the macro is stopped via the Stop
    /// command or global hotkey), ignoring <see cref="Count"/>.
    /// </summary>
    public bool Infinite
    {
        get;
        set => SetValueProperty(ref field, value);
    }

    public override MacroStepType StepType => MacroStepType.RepeatStart;
    public override string Icon => Glyph(0xF0456); // mdi-repeat
    public override string TypeText => "Repeat Start";

    public override string ValueText
    {
        get
        {
            var count = Infinite ? "∞" : $"{Count}×";
            return LoopDelayMilliseconds > 0 ? $"{count}  (+{LoopDelayMilliseconds} ms)" : count;
        }
    }
}

/// <summary>Marks the end of the block opened by the nearest open <see cref="RepeatStartStep"/>.</summary>
public class RepeatEndStep : MacroStep
{
    public override MacroStepType StepType => MacroStepType.RepeatEnd;
    public override string Icon => Glyph(0xF0457); // mdi-repeat-off
    public override string TypeText => "Repeat End";
    public override string ValueText => string.Empty;
}

/// <summary>
/// Sets, increments, or decrements a local macro variable. Variables live only for the
/// duration of one macro run and are referenced elsewhere as <c>{name}</c> placeholders
/// (expanded in Type Text, Command, and condition operands). Increment/Decrement treat the
/// variable as a number (missing/non-numeric ⇒ 0) and apply <see cref="Value"/> as the
/// amount (defaults to 1), enabling counters inside repeat blocks.
/// </summary>
public class SetVariableStep : MacroStep
{
    /// <summary>Variable name (case-insensitive), without the surrounding braces.</summary>
    public string Name
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public VariableOperation Operation
    {
        get;
        set => SetValueProperty(ref field, value);
    } = VariableOperation.Set;

    /// <summary>
    /// For Set: the literal value (may contain <c>{placeholders}</c>). For Increment/Decrement:
    /// the numeric amount (blank ⇒ 1).
    /// </summary>
    public string Value
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    /// <summary>All operations — bound by the editor's ComboBox.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public static VariableOperation[] AllOperations { get; } = Enum.GetValues<VariableOperation>();

    public override MacroStepType StepType => MacroStepType.SetVariable;
    public override string Icon => Glyph(0xF0AE7); // mdi-variable
    public override string TypeText => "Set Variable";

    public override string ValueText => Operation switch
    {
        VariableOperation.Increment => $"{Name} += {(string.IsNullOrWhiteSpace(Value) ? "1" : Value)}",
        VariableOperation.Decrement => $"{Name} -= {(string.IsNullOrWhiteSpace(Value) ? "1" : Value)}",
        _ => $"{Name} = {Value}"
    };
}

/// <summary>
/// Marks the start of a conditional block. Steps up to the matching <see cref="ElseStep"/>
/// run when <see cref="Condition"/> is true; steps between Else and the matching
/// <see cref="EndIfStep"/> run when it is false. Markers are matched by order at run time
/// (nesting supported, including inside Repeat blocks). An unmatched If runs its body to the
/// end of the macro.
/// </summary>
public class IfStep : MacroStep
{
    public IfStep()
    {
        Condition = new MacroCondition();
    }

    /// <summary>The test evaluated when the block is reached. Never null after construction.</summary>
    public MacroCondition Condition
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.PropertyChanged -= OnConditionChanged;
            field = value ?? new MacroCondition();
            field.PropertyChanged += OnConditionChanged;
            OnValueChanged();
        }
    } = new();

    // Bubble the nested condition's changes so the panel summary refreshes live (and after
    // JSON Populate replaces the condition, the new instance stays wired up).
    private void OnConditionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Raise the persisted Condition property (not just ValueText, which the editor treats
        // as non-persisted UI state) so editing the condition schedules a save.
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ValueText));
    }

    public override MacroStepType StepType => MacroStepType.If;
    public override string Icon => Glyph(0xF0EAA); // mdi-source-branch
    public override string TypeText => "If";
    public override string ValueText => Condition?.Summary ?? string.Empty;
}

/// <summary>Separates the true and false branches of the nearest open <see cref="IfStep"/>.</summary>
public class ElseStep : MacroStep
{
    public override MacroStepType StepType => MacroStepType.Else;
    public override string Icon => Glyph(0xF0EAA); // mdi-source-branch
    public override string TypeText => "Else";
    public override string ValueText => string.Empty;
}

/// <summary>Marks the end of the block opened by the nearest open <see cref="IfStep"/>.</summary>
public class EndIfStep : MacroStep
{
    public override MacroStepType StepType => MacroStepType.EndIf;
    public override string Icon => Glyph(0xF0EAA); // mdi-source-branch
    public override string TypeText => "End If";
    public override string ValueText => string.Empty;
}

/// <summary>
/// Pauses the macro until <see cref="Condition"/> becomes true or the timeout elapses,
/// polling every <see cref="PollIntervalMilliseconds"/>. On timeout, <see cref="OnTimeout"/>
/// either aborts the macro (Fail) or lets it continue. A timeout of 0 waits indefinitely
/// (until the macro is stopped). Covers "wait for a process / window to appear or disappear".
/// </summary>
public class WaitForConditionStep : MacroStep
{
    public MacroCondition Condition
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field?.PropertyChanged -= OnConditionChanged;
            field = value ?? new MacroCondition();
            field.PropertyChanged += OnConditionChanged;
            OnValueChanged();
        }
    } = new();

    private void OnConditionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Raise the persisted Condition property (not just ValueText, which the editor treats
        // as non-persisted UI state) so editing the condition schedules a save.
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ValueText));
    }

    /// <summary>Maximum time to wait; 0 means wait forever (until stopped).</summary>
    public int TimeoutMilliseconds
    {
        get;
        set => SetValueProperty(ref field, value);
    } = 10000;

    /// <summary>How often the condition is re-checked while waiting.</summary>
    public int PollIntervalMilliseconds
    {
        get;
        set => SetValueProperty(ref field, value);
    } = 250;

    public WaitTimeoutBehavior OnTimeout
    {
        get;
        set => SetValueProperty(ref field, value);
    } = WaitTimeoutBehavior.Fail;

    /// <summary>All timeout behaviours — bound by the editor's ComboBox.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public static WaitTimeoutBehavior[] AllTimeoutBehaviors { get; } = Enum.GetValues<WaitTimeoutBehavior>();

    public override MacroStepType StepType => MacroStepType.WaitForCondition;
    public override string Icon => Glyph(0xF0150); // mdi-clock-outline
    public override string TypeText => "Wait For";

    public override string ValueText
    {
        get
        {
            var timeout = TimeoutMilliseconds > 0 ? $"≤ {TimeoutMilliseconds} ms" : "∞";
            return $"{Condition?.Summary} ({timeout})";
        }
    }
}

/// <summary>
/// Pauses the macro and asks the user for a text value, storing it in the named variable
/// for later <c>{name}</c> use. Cancelling the prompt leaves the variable unchanged and the
/// macro continues. The prompt is shown on the UI thread and closes if the macro is stopped.
/// </summary>
public class PromptStep : MacroStep
{
    /// <summary>Prompt text shown to the user.</summary>
    public string Message
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    /// <summary>Variable the entered text is stored in.</summary>
    public string VariableName
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    /// <summary>Pre-filled value in the input box.</summary>
    public string DefaultValue
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.Prompt;
    public override string Icon => Glyph(0xF0CB6); // mdi-tooltip-edit
    public override string TypeText => "Prompt";
    public override string ValueText =>
        string.IsNullOrWhiteSpace(VariableName) ? Message : $"{VariableName} ← \"{Message}\"";
}

/// <summary>Runs an arbitrary LoupixDeck command string or shell command.</summary>
public class CommandStep : MacroStep
{
    public string CommandString
    {
        get;
        set => SetValueProperty(ref field, value);
    } = string.Empty;

    public override MacroStepType StepType => MacroStepType.Command;
    public override string Icon => Glyph(0xF018D); // mdi-console
    public override string TypeText => "Command";
    public override string ValueText => CommandString ?? string.Empty;
}
