using System.Text;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Per-run state for a single macro execution: user-defined variables and the set of
/// keys / mouse buttons currently held down. The held sets are authoritative so the
/// runner can release everything still pressed when the macro ends or is cancelled
/// (guaranteed cleanup), without double-releasing keys the macro already lifted itself.
/// </summary>
public sealed class MacroContext
{
    /// <summary>
    /// Case-insensitive variable store. Values are kept as strings; numeric operations
    /// parse on demand. Populated by SetVariable / Prompt steps and read via <see cref="Expand"/>.
    /// </summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The physical button / touch press that started this run (#185), or null when the macro
    /// was triggered some other way (editor test run, stop hotkey, plugin, IPC).
    /// </summary>
    public TriggerPress TriggerPress { get; init; }

    /// <summary>True while the button that triggered this macro is still held down.</summary>
    public bool IsTriggerHeld => TriggerPress?.IsHeld == true;

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

    /// <summary>
    /// Replaces <c>{name}</c> placeholders in <paramref name="template"/> with the matching
    /// variable value (case-insensitive). Unknown names expand to an empty string. Called
    /// lazily at use time so loop counters and mid-run mutations resolve to current values.
    /// Same rules as the former <c>{([^{}]+)}</c> pattern: nested braces and empty
    /// <c>{}</c> are left as-is.
    /// </summary>
    public string Expand(string template)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        ReadOnlySpan<char> span = template;
        int firstOpen = span.IndexOf('{');
        if (firstOpen < 0)
            return template;

        var lookup = Variables.GetAlternateLookup<ReadOnlySpan<char>>();
        var builder = new StringBuilder(template.Length);
        int i = 0;
        while (i < span.Length)
        {
            int openRel = span.Slice(i).IndexOf('{');
            if (openRel < 0)
            {
                builder.Append(span.Slice(i));
                break;
            }

            int open = i + openRel;
            builder.Append(span.Slice(i, openRel));

            int closeRel = span.Slice(open + 1).IndexOf('}');
            if (closeRel < 0)
            {
                builder.Append(span.Slice(open));
                break;
            }

            ReadOnlySpan<char> inner = span.Slice(open + 1, closeRel);
            if (inner.IsEmpty || inner.Contains('{'))
            {
                // "{}" or "{a{b}" — not a placeholder. Emit the '{' and keep scanning.
                builder.Append('{');
                i = open + 1;
                continue;
            }

            ReadOnlySpan<char> name = inner.Trim();
            if (lookup.TryGetValue(name, out var value) && value != null)
                builder.Append(value);

            i = open + 1 + closeRel + 1;
        }

        return builder.ToString();
    }
}
