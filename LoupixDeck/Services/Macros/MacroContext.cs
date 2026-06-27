using System.Text.RegularExpressions;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Per-run state for a single macro execution: user-defined variables and the set of
/// keys / mouse buttons currently held down. The held sets are authoritative so the
/// runner can release everything still pressed when the macro ends or is cancelled
/// (guaranteed cleanup), without double-releasing keys the macro already lifted itself.
/// </summary>
public sealed partial class MacroContext
{
    /// <summary>
    /// Case-insensitive variable store. Values are kept as strings; numeric operations
    /// parse on demand. Populated by SetVariable / Prompt steps and read via <see cref="Expand"/>.
    /// </summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Insertion-ordered so cleanup can release in reverse acquisition order.
    private readonly List<string> _heldKeys = [];
    private readonly List<MouseButton> _heldButtons = [];

    public IReadOnlyList<string> HeldKeys => _heldKeys;
    public IReadOnlyList<MouseButton> HeldButtons => _heldButtons;

    public void MarkKeyDown(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!_heldKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            _heldKeys.Add(key);
    }

    public void MarkKeyUp(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _heldKeys.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
    }

    public void MarkButtonDown(MouseButton button)
    {
        if (!_heldButtons.Contains(button))
            _heldButtons.Add(button);
    }

    public void MarkButtonUp(MouseButton button)
    {
        _heldButtons.Remove(button);
    }

    // Matches {name} placeholders; the name is anything but braces so nesting is rejected.

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern { get; }

    /// <summary>
    /// Replaces <c>{name}</c> placeholders in <paramref name="template"/> with the matching
    /// variable value (case-insensitive). Unknown names expand to an empty string. Called
    /// lazily at use time so loop counters and mid-run mutations resolve to current values.
    /// </summary>
    public string Expand(string template)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0)
            return template;

        return PlaceholderPattern.Replace(template, match =>
        {
            var name = match.Groups[1].Value.Trim();
            return Variables.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;
        });
    }
}
