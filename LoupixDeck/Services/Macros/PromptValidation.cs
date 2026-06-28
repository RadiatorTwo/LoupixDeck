using System.Globalization;
using System.Text.RegularExpressions;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Single source of truth for validating and normalising the value a user enters into a macro
/// <see cref="Models.Macros.PromptStep"/>. Used by the runtime prompt dialog (to block OK and show
/// an error) so the value written into a variable always satisfies the step's restrictions.
/// </summary>
public static class PromptValidation
{
    /// <summary>
    /// Validates <paramref name="input"/> against the request's input type and restrictions.
    /// On success returns true and yields the canonical value to store (e.g. <c>"true"</c>/<c>"false"</c>
    /// for booleans, an invariant round-trip for numbers). On failure returns false with a
    /// user-facing <paramref name="error"/> message.
    /// </summary>
    public static bool TryValidate(MacroPromptRequest request, string input, out string normalized, out string error)
    {
        normalized = input ?? string.Empty;
        error = string.Empty;
        string trimmed = normalized.Trim();
        bool isEmpty = string.IsNullOrWhiteSpace(trimmed);

        switch (request.InputType)
        {
            case PromptInputType.Text:
                return ValidateText(request, normalized, isEmpty, out error);

            case PromptInputType.Integer:
                return ValidateInteger(request, trimmed, isEmpty, out normalized, out error);

            case PromptInputType.Decimal:
                return ValidateDecimal(request, trimmed, isEmpty, out normalized, out error);

            case PromptInputType.Boolean:
                return ValidateBoolean(trimmed, out normalized, out error);

            case PromptInputType.Selection:
                return ValidateSelection(request, trimmed, isEmpty, out normalized, out error);

            default:
                return true;
        }
    }

    private static bool ValidateText(MacroPromptRequest request, string input, bool isEmpty, out string error)
    {
        error = string.Empty;

        if (isEmpty)
        {
            if (!request.AllowEmpty)
            {
                error = "A value is required.";
                return false;
            }

            return true;
        }

        if (request.MinLength is { } min && input.Length < min)
        {
            error = $"Enter at least {min} character(s).";
            return false;
        }

        if (request.MaxLength is { } max && input.Length > max)
        {
            error = $"Enter at most {max} character(s).";
            return false;
        }

        if (!string.IsNullOrEmpty(request.ValidationRegex))
        {
            bool matches;
            try
            {
                matches = Regex.IsMatch(input, request.ValidationRegex);
            }
            catch (ArgumentException)
            {
                // A broken pattern shouldn't trap the user — treat it as no constraint.
                matches = true;
            }

            if (!matches)
            {
                error = "The value does not match the required format.";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateInteger(MacroPromptRequest request, string trimmed, bool isEmpty,
        out string normalized, out string error)
    {
        normalized = trimmed;
        error = string.Empty;

        if (isEmpty)
        {
            if (!request.AllowEmpty)
            {
                error = "A whole number is required.";
                return false;
            }

            normalized = string.Empty;
            return true;
        }

        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
        {
            error = "Enter a whole number (no decimals).";
            return false;
        }

        if (!CheckNumericRange(request, value, out error))
            return false;

        normalized = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool ValidateDecimal(MacroPromptRequest request, string trimmed, bool isEmpty,
        out string normalized, out string error)
    {
        normalized = trimmed;
        error = string.Empty;

        if (isEmpty)
        {
            if (!request.AllowEmpty)
            {
                error = "A number is required.";
                return false;
            }

            normalized = string.Empty;
            return true;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            error = "Enter a valid number.";
            return false;
        }

        if (!CheckNumericRange(request, value, out error))
            return false;

        normalized = value.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool CheckNumericRange(MacroPromptRequest request, double value, out string error)
    {
        error = string.Empty;

        if (!request.AllowNegative && value < 0)
        {
            error = "Negative values are not allowed.";
            return false;
        }

        if (!request.AllowZero && value == 0)
        {
            error = "Zero is not allowed.";
            return false;
        }

        if (request.Minimum is { } min && value < min)
        {
            error = $"Enter a value of at least {min.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (request.Maximum is { } max && value > max)
        {
            error = $"Enter a value of at most {max.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        return true;
    }

    private static bool ValidateBoolean(string trimmed, out string normalized, out string error)
    {
        error = string.Empty;

        switch (trimmed.ToLowerInvariant())
        {
            case "true" or "yes" or "1" or "on":
                normalized = "true";
                return true;
            case "false" or "no" or "0" or "off" or "":
                normalized = "false";
                return true;
            default:
                normalized = trimmed;
                error = "Choose yes or no.";
                return false;
        }
    }

    private static bool ValidateSelection(MacroPromptRequest request, string trimmed, bool isEmpty,
        out string normalized, out string error)
    {
        normalized = trimmed;
        error = string.Empty;

        if (isEmpty)
        {
            if (!request.AllowEmpty)
            {
                error = "Select a value.";
                return false;
            }

            normalized = string.Empty;
            return true;
        }

        foreach (string item in request.SelectionItems)
        {
            if (string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = item; // canonical casing from the list
                return true;
            }
        }

        error = "Select a value from the list.";
        return false;
    }
}