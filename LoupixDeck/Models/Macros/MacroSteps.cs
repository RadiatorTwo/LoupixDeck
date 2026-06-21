namespace LoupixDeck.Models.Macros;

/// <summary>Types a text string via the virtual keyboard.</summary>
public class TextStep : MacroStep
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.Text;
    public override string Icon => Glyph(0xF030C); // mdi-keyboard
    public override string TypeText => "Type Text";
    public override string ValueText => Text ?? string.Empty;
}

/// <summary>Presses a key combination, e.g. "Ctrl+Shift+Esc".</summary>
public class KeyCombinationStep : MacroStep
{
    private string _keys = string.Empty;

    /// <summary>Key names joined with '+', same syntax as System.KeyCombination.</summary>
    public string Keys
    {
        get => _keys;
        set
        {
            if (_keys == value) return;
            _keys = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.KeyCombination;
    public override string Icon => Glyph(0xF0317); // mdi-keyboard-variant
    public override string TypeText => "Key Combination";
    public override string ValueText => Keys ?? string.Empty;
}

/// <summary>Waits for a fixed amount of time before the next step.</summary>
public class DelayStep : MacroStep
{
    private int _milliseconds = 100;

    public int Milliseconds
    {
        get => _milliseconds;
        set
        {
            if (_milliseconds == value) return;
            _milliseconds = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.Delay;
    public override string Icon => Glyph(0xF051F); // mdi-timer-sand
    public override string TypeText => "Delay";
    public override string ValueText => $"{Milliseconds} ms";
}

/// <summary>Presses (and holds) a single key without releasing it.</summary>
public class KeyDownStep : MacroStep
{
    private string _key = string.Empty;

    public string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.KeyDown;
    public override string Icon => Glyph(0xF013C); // mdi-chevron-double-down
    public override string TypeText => "Key Down";
    public override string ValueText => Key ?? string.Empty;
}

/// <summary>Releases a key previously held down by a <see cref="KeyDownStep"/>.</summary>
public class KeyUpStep : MacroStep
{
    private string _key = string.Empty;

    public string Key
    {
        get => _key;
        set
        {
            if (_key == value) return;
            _key = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.KeyUp;
    public override string Icon => Glyph(0xF013F); // mdi-chevron-double-up
    public override string TypeText => "Key Up";
    public override string ValueText => Key ?? string.Empty;
}

/// <summary>Performs a mouse action (click, button down/up, move, scroll).</summary>
public class MouseStep : MacroStep
{
    /// <summary>All selectable actions/buttons — bound by the editor's ComboBoxes.</summary>
    public static MouseStepAction[] AllActions { get; } = Enum.GetValues<MouseStepAction>();

    public static MouseButton[] AllButtons { get; } = Enum.GetValues<MouseButton>();

    private MouseStepAction _action = MouseStepAction.Click;
    private MouseButton _button = MouseButton.Left;
    private int _x;
    private int _y;
    private int _amount = 1;

    public MouseStepAction Action
    {
        get => _action;
        set
        {
            if (_action == value) return;
            _action = value;
            OnValueChanged();
            OnPropertyChanged(nameof(ShowsButton));
            OnPropertyChanged(nameof(ShowsCoordinates));
            OnPropertyChanged(nameof(ShowsAmount));
            OnPropertyChanged(nameof(ShowsAbsoluteHint));
        }
    }

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
        get => _button;
        set
        {
            if (_button == value) return;
            _button = value;
            OnValueChanged();
        }
    }

    /// <summary>X coordinate: relative delta for MoveRelative, screen pixel for MoveAbsolute.</summary>
    public int X
    {
        get => _x;
        set
        {
            if (_x == value) return;
            _x = value;
            OnValueChanged();
        }
    }

    /// <summary>Y coordinate: relative delta for MoveRelative, screen pixel for MoveAbsolute.</summary>
    public int Y
    {
        get => _y;
        set
        {
            if (_y == value) return;
            _y = value;
            OnValueChanged();
        }
    }

    /// <summary>Scroll amount in wheel detents (positive = up, negative = down).</summary>
    public int Amount
    {
        get => _amount;
        set
        {
            if (_amount == value) return;
            _amount = value;
            OnValueChanged();
        }
    }

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
    private int _count = 2;

    /// <summary>Number of times the block runs (clamped to at least 1 at execution time).</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            OnValueChanged();
        }
    }

    private int _loopDelayMilliseconds;

    /// <summary>Optional pause inserted between iterations (not after the last one).</summary>
    public int LoopDelayMilliseconds
    {
        get => _loopDelayMilliseconds;
        set
        {
            if (_loopDelayMilliseconds == value) return;
            _loopDelayMilliseconds = value;
            OnValueChanged();
        }
    }

    private bool _infinite;

    /// <summary>
    /// When true the block repeats forever (until the macro is stopped via the Stop
    /// command or global hotkey), ignoring <see cref="Count"/>.
    /// </summary>
    public bool Infinite
    {
        get => _infinite;
        set
        {
            if (_infinite == value) return;
            _infinite = value;
            OnValueChanged();
        }
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
    private string _name = string.Empty;

    /// <summary>Variable name (case-insensitive), without the surrounding braces.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnValueChanged();
        }
    }

    private VariableOperation _operation = VariableOperation.Set;

    public VariableOperation Operation
    {
        get => _operation;
        set
        {
            if (_operation == value) return;
            _operation = value;
            OnValueChanged();
        }
    }

    private string _value = string.Empty;

    /// <summary>
    /// For Set: the literal value (may contain <c>{placeholders}</c>). For Increment/Decrement:
    /// the numeric amount (blank ⇒ 1).
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnValueChanged();
        }
    }

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
    private MacroCondition _condition;

    public IfStep()
    {
        Condition = new MacroCondition();
    }

    /// <summary>The test evaluated when the block is reached. Never null after construction.</summary>
    public MacroCondition Condition
    {
        get => _condition;
        set
        {
            if (ReferenceEquals(_condition, value)) return;
            _condition?.PropertyChanged -= OnConditionChanged;
            _condition = value ?? new MacroCondition();
            _condition.PropertyChanged += OnConditionChanged;
            OnValueChanged();
        }
    }

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
    private MacroCondition _condition;

    public WaitForConditionStep()
    {
        Condition = new MacroCondition();
    }

    public MacroCondition Condition
    {
        get => _condition;
        set
        {
            if (ReferenceEquals(_condition, value)) return;
            _condition?.PropertyChanged -= OnConditionChanged;
            _condition = value ?? new MacroCondition();
            _condition.PropertyChanged += OnConditionChanged;
            OnValueChanged();
        }
    }

    private void OnConditionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Raise the persisted Condition property (not just ValueText, which the editor treats
        // as non-persisted UI state) so editing the condition schedules a save.
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ValueText));
    }

    private int _timeoutMilliseconds = 10000;

    /// <summary>Maximum time to wait; 0 means wait forever (until stopped).</summary>
    public int TimeoutMilliseconds
    {
        get => _timeoutMilliseconds;
        set
        {
            if (_timeoutMilliseconds == value) return;
            _timeoutMilliseconds = value;
            OnValueChanged();
        }
    }

    private int _pollIntervalMilliseconds = 250;

    /// <summary>How often the condition is re-checked while waiting.</summary>
    public int PollIntervalMilliseconds
    {
        get => _pollIntervalMilliseconds;
        set
        {
            if (_pollIntervalMilliseconds == value) return;
            _pollIntervalMilliseconds = value;
            OnValueChanged();
        }
    }

    private WaitTimeoutBehavior _onTimeout = WaitTimeoutBehavior.Fail;

    public WaitTimeoutBehavior OnTimeout
    {
        get => _onTimeout;
        set
        {
            if (_onTimeout == value) return;
            _onTimeout = value;
            OnValueChanged();
        }
    }

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
    private string _message = string.Empty;

    /// <summary>Prompt text shown to the user.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (_message == value) return;
            _message = value;
            OnValueChanged();
        }
    }

    private string _variableName = string.Empty;

    /// <summary>Variable the entered text is stored in.</summary>
    public string VariableName
    {
        get => _variableName;
        set
        {
            if (_variableName == value) return;
            _variableName = value;
            OnValueChanged();
        }
    }

    private string _defaultValue = string.Empty;

    /// <summary>Pre-filled value in the input box.</summary>
    public string DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (_defaultValue == value) return;
            _defaultValue = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.Prompt;
    public override string Icon => Glyph(0xF0CB6); // mdi-tooltip-edit
    public override string TypeText => "Prompt";
    public override string ValueText =>
        string.IsNullOrWhiteSpace(VariableName) ? Message : $"{VariableName} ← \"{Message}\"";
}

/// <summary>Runs an arbitrary LoupixDeck command string or shell command.</summary>
public class CommandStep : MacroStep
{
    private string _commandString = string.Empty;

    public string CommandString
    {
        get => _commandString;
        set
        {
            if (_commandString == value) return;
            _commandString = value;
            OnValueChanged();
        }
    }

    public override MacroStepType StepType => MacroStepType.Command;
    public override string Icon => Glyph(0xF018D); // mdi-console
    public override string TypeText => "Command";
    public override string ValueText => CommandString ?? string.Empty;
}
