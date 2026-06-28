using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

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
public partial class RepeatStartStep : MacroStep
{
    /// <summary>Number of times the block runs (clamped to at least 1 at execution time).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial int Count { get; set; } = 2;

    /// <summary>Optional pause inserted between iterations (not after the last one).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial int LoopDelayMilliseconds { get; set; }

    /// <summary>
    /// When true the block repeats forever (until the macro is stopped via the Stop
    /// command or global hotkey), ignoring <see cref="Count"/> and <see cref="CountVariable"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(IsFixed))]
    [NotifyPropertyChangedFor(nameof(IsVariableCount))]
    [NotifyPropertyChangedFor(nameof(IsInfinite))]
    public partial bool Infinite { get; set; }

    /// <summary>
    /// Where the count comes from when not <see cref="Infinite"/>: a literal <see cref="Count"/>
    /// or the integer value of <see cref="CountVariable"/>. Defaults to <see cref="RepeatCountMode.Fixed"/>
    /// so files saved before this field existed keep their original fixed-count behaviour.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(IsFixed))]
    [NotifyPropertyChangedFor(nameof(IsVariableCount))]
    [NotifyPropertyChangedFor(nameof(IsInfinite))]
    public partial RepeatCountMode CountMode { get; set; } = RepeatCountMode.Fixed;

    /// <summary>
    /// Variable whose value supplies the repeat count when <see cref="CountMode"/> is
    /// <see cref="RepeatCountMode.Variable"/>. Accepts a bare name (<c>repeatCount</c>) or a
    /// placeholder (<c>{repeatCount}</c>); both are resolved at run time.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial string CountVariable { get; set; } = string.Empty;

    /// <summary>
    /// UI-only three-way selector projected onto the persisted <see cref="Infinite"/> +
    /// <see cref="CountMode"/> fields (<see cref="Infinite"/> wins). Not serialized.
    /// </summary>
    [JsonIgnore]
    public RepeatMode Mode
    {
        get => Infinite ? RepeatMode.Infinite
            : CountMode == RepeatCountMode.Variable ? RepeatMode.Variable
            : RepeatMode.Fixed;
        set
        {
            // Project the selector back onto the persisted fields. The generated
            // Infinite/CountMode setters self-guard against no-ops and (via
            // [NotifyPropertyChangedFor]) raise Mode/IsFixed/IsVariableCount/
            // IsInfinite/ValueText for us.
            Infinite = value == RepeatMode.Infinite;
            CountMode = value == RepeatMode.Variable ? RepeatCountMode.Variable : RepeatCountMode.Fixed;
        }
    }

    /// <summary>True when a literal count is used (editor visibility for the number box).</summary>
    [JsonIgnore]
    public bool IsFixed => Mode == RepeatMode.Fixed;

    /// <summary>True when a variable supplies the count (editor visibility for the variable box).</summary>
    [JsonIgnore]
    public bool IsVariableCount => Mode == RepeatMode.Variable;

    /// <summary>True when the block repeats forever (editor visibility helper).</summary>
    [JsonIgnore]
    public bool IsInfinite => Mode == RepeatMode.Infinite;

    /// <summary>All selectable repeat modes — bound by the editor's ComboBox.</summary>
    [JsonIgnore]
    public static ImmutableArray<RepeatMode> AllModes { get; } =
        ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<RepeatMode>());

    public override MacroStepType StepType => MacroStepType.RepeatStart;
    public override string Icon => Glyph(0xF0456); // mdi-repeat
    public override string TypeText => "Repeat Start";

    public override string ValueText
    {
        get
        {
            string count = Mode switch
            {
                RepeatMode.Infinite => "∞",
                RepeatMode.Variable => $"{CountVariable}×",
                _ => $"{Count}×"
            };
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
public partial class PromptStep : MacroStep
{
    /// <summary>Prompt text shown to the user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>Variable the entered text is stored in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial string VariableName { get; set; } = string.Empty;

    /// <summary>Pre-filled value in the input box (and the preselected item for a selection).</summary>
    [ObservableProperty]
    public partial string DefaultValue { get; set; } = string.Empty;

    /// <summary>
    /// How the entered value is validated and stored. Defaults to <see cref="PromptInputType.Text"/>,
    /// so prompts saved before this field existed behave exactly like a plain text prompt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumericType))]
    [NotifyPropertyChangedFor(nameof(IsTextType))]
    [NotifyPropertyChangedFor(nameof(IsSelectionType))]
    [NotifyPropertyChangedFor(nameof(IsBooleanType))]
    public partial PromptInputType InputType { get; set; } = PromptInputType.Text;

    /// <summary>When false an empty/whitespace answer is rejected (re-prompted).</summary>
    [ObservableProperty]
    public partial bool AllowEmpty { get; set; } = true;

    /// <summary>Inclusive lower bound for Integer/Decimal input (null = no bound).</summary>
    [ObservableProperty]
    public partial double? Minimum { get; set; }

    /// <summary>Inclusive upper bound for Integer/Decimal input (null = no bound).</summary>
    [ObservableProperty]
    public partial double? Maximum { get; set; }

    /// <summary>When false a value of 0 is rejected (Integer/Decimal).</summary>
    [ObservableProperty]
    public partial bool AllowZero { get; set; } = true;

    /// <summary>When false a negative value is rejected (Integer/Decimal).</summary>
    [ObservableProperty]
    public partial bool AllowNegative { get; set; } = true;

    /// <summary>Minimum length for Text input (null = no minimum).</summary>
    [ObservableProperty]
    public partial int? MinLength { get; set; }

    /// <summary>Maximum length for Text input (null = no maximum).</summary>
    [ObservableProperty]
    public partial int? MaxLength { get; set; }

    /// <summary>Optional .NET regular expression the Text answer must fully match (empty = no check).</summary>
    [ObservableProperty]
    public partial string ValidationRegex { get; set; } = string.Empty;

    /// <summary>Allowed values for a <see cref="PromptInputType.Selection"/> prompt.</summary>
    public ObservableCollection<string> SelectionItems { get; set; } = [];

    /// <summary>
    /// Editor-friendly newline-separated view of <see cref="SelectionItems"/> (one value per line).
    /// Not serialized — the list itself is.
    /// </summary>
    [JsonIgnore]
    public string SelectionItemsText
    {
        get => string.Join(Environment.NewLine, SelectionItems);
        set
        {
            SelectionItems.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (string line in value.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0)
                        SelectionItems.Add(trimmed);
                }
            }

            OnValueChanged();
        }
    }

    /// <summary>True for Integer/Decimal input (editor visibility for numeric options).</summary>
    [JsonIgnore]
    public bool IsNumericType => InputType is PromptInputType.Integer or PromptInputType.Decimal;

    /// <summary>True for Text input (editor visibility for length/regex options).</summary>
    [JsonIgnore]
    public bool IsTextType => InputType == PromptInputType.Text;

    /// <summary>True for Selection input (editor visibility for the value list).</summary>
    [JsonIgnore]
    public bool IsSelectionType => InputType == PromptInputType.Selection;

    /// <summary>True for Boolean input.</summary>
    [JsonIgnore]
    public bool IsBooleanType => InputType == PromptInputType.Boolean;

    /// <summary>All selectable input types — bound by the editor's ComboBox.</summary>
    [JsonIgnore]
    public static ImmutableArray<PromptInputType> AllInputTypes { get; } =
        ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<PromptInputType>());

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