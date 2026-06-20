using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models.Macros;

/// <summary>What a <see cref="MacroCondition"/> tests.</summary>
public enum ConditionType
{
    /// <summary>True when a process with the given name is running.</summary>
    ProcessRunning,

    /// <summary>True when the foreground window's process matches the given name.</summary>
    ActiveWindowProcessIs,

    /// <summary>True when the foreground window's title contains the given text.</summary>
    ActiveWindowTitleContains,

    /// <summary>Compares a variable against a value using <see cref="MacroCondition.Operator"/>.</summary>
    Variable
}

/// <summary>Comparison used by <see cref="ConditionType.Variable"/> conditions.</summary>
public enum ConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual
}

/// <summary>
/// A boolean test evaluated at run time by If / Wait steps. Process and window tests use
/// <see cref="Target"/> as the process name / title substring. Variable tests use
/// <see cref="Target"/> as the variable name, <see cref="Operator"/> as the comparison, and
/// <see cref="Operand"/> as the value. All string fields support <c>{name}</c> placeholders.
/// <see cref="Negate"/> inverts the result. Persisted inline inside its owning step.
/// </summary>
[ObservableObject]
public partial class MacroCondition
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVariable))]
    [NotifyPropertyChangedFor(nameof(TargetLabel))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial ConditionType Type { get; set; } = ConditionType.ProcessRunning;

    /// <summary>Process name, title substring, or variable name depending on <see cref="Type"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    /// <summary>Value the variable is compared against (variable conditions only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Operand { get; set; } = string.Empty;

    /// <summary>Inverts the test (e.g. "process is NOT running").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Negate { get; set; }

    /// <summary>All selectable values — bound by the editor's ComboBoxes.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public static ImmutableArray<ConditionType> AllTypes { get; } = ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<ConditionType>());

    [Newtonsoft.Json.JsonIgnore]
    public static ImmutableArray<ConditionOperator> AllOperators { get; } = ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<ConditionOperator>());

    /// <summary>True when the variable-specific operator/operand fields apply (editor visibility).</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool IsVariable => Type == ConditionType.Variable;

    /// <summary>Editor label for the <see cref="Target"/> field, which differs per type.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string TargetLabel => Type switch
    {
        ConditionType.ProcessRunning => "Process",
        ConditionType.ActiveWindowProcessIs => "Process",
        ConditionType.ActiveWindowTitleContains => "Title contains",
        ConditionType.Variable => "Variable",
        _ => "Value"
    };

    /// <summary>One-line human-readable summary shown in step previews.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string Summary
    {
        get
        {
            var not = Negate ? "not " : string.Empty;
            return Type switch
            {
                ConditionType.ProcessRunning => $"{not}process '{Target}' running",
                ConditionType.ActiveWindowProcessIs => $"active app is {not}'{Target}'",
                ConditionType.ActiveWindowTitleContains => $"title {not}contains '{Target}'",
                ConditionType.Variable => $"{not}({Target} {OperatorText} {Operand})",
                _ => string.Empty
            };
        }
    }

    private string OperatorText => Operator switch
    {
        ConditionOperator.Equals => "==",
        ConditionOperator.NotEquals => "!=",
        ConditionOperator.Contains => "contains",
        ConditionOperator.GreaterThan => ">",
        ConditionOperator.GreaterOrEqual => ">=",
        ConditionOperator.LessThan => "<",
        ConditionOperator.LessOrEqual => "<=",
        _ => "?"
    };
}
