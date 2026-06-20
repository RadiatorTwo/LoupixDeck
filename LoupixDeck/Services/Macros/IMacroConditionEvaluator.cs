using System.Diagnostics;
using System.Globalization;
using LoupixDeck.Models.Macros;
using LoupixDeck.Services.ActiveWindow;

namespace LoupixDeck.Services.Macros;

/// <summary>Evaluates a <see cref="MacroCondition"/> against the current system / run state.</summary>
public interface IMacroConditionEvaluator
{
    bool Evaluate(MacroCondition condition, MacroContext context);
}

/// <inheritdoc cref="IMacroConditionEvaluator"/>
public sealed class MacroConditionEvaluator(IActiveWindowState activeWindow) : IMacroConditionEvaluator
{
    public bool Evaluate(MacroCondition condition, MacroContext context)
    {
        if (condition == null)
            return false;

        var result = condition.Type switch
        {
            ConditionType.ProcessRunning => IsProcessRunning(context.Expand(condition.Target)),
            ConditionType.ActiveWindowProcessIs => ProcessNameEquals(
                activeWindow.Current?.ProcessName, context.Expand(condition.Target)),
            ConditionType.ActiveWindowTitleContains => ContainsIgnoreCase(
                activeWindow.Current?.Title, context.Expand(condition.Target)),
            ConditionType.Variable => EvaluateVariable(condition, context),
            _ => false
        };

        return condition.Negate ? !result : result;
    }

    private static bool IsProcessRunning(string name)
    {
        var normalized = Normalize(name);
        if (string.IsNullOrEmpty(normalized))
            return false;

        try
        {
            // GetProcessesByName matches the bare name (no path, no ".exe") on both OSes.
            return Process.GetProcessesByName(normalized).Length > 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MacroCondition] Process lookup '{normalized}' failed: {ex.Message}");
            return false;
        }
    }

    private static bool ProcessNameEquals(string actual, string expected)
    {
        var a = Normalize(actual);
        var b = Normalize(expected);
        return !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return false;
        return (haystack ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EvaluateVariable(MacroCondition condition, MacroContext context)
    {
        var name = (condition.Target ?? string.Empty).Trim();
        context.Variables.TryGetValue(name, out var left);
        left ??= string.Empty;
        var right = context.Expand(condition.Operand) ?? string.Empty;

        switch (condition.Operator)
        {
            case ConditionOperator.Equals:
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            case ConditionOperator.NotEquals:
                return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            case ConditionOperator.Contains:
                return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Numeric comparisons — non-numeric operands make the test false rather than throwing.
        if (!TryParseNumber(left, out var l) || !TryParseNumber(right, out var r))
            return false;

        return condition.Operator switch
        {
            ConditionOperator.GreaterThan => l > r,
            ConditionOperator.GreaterOrEqual => l >= r,
            ConditionOperator.LessThan => l < r,
            ConditionOperator.LessOrEqual => l <= r,
            _ => false
        };
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    /// <summary>Strips a trailing ".exe" so Windows and Linux process names are portable.</summary>
    private static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
}
