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
