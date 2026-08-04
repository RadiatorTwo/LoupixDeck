using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// In-memory store of the user-defined macros backed by macros.json. Loaded once
/// at startup; all reads (execution, menus) are served from memory.
/// </summary>
public interface IMacroManager
{
    /// <summary>All currently defined macros.</summary>
    IReadOnlyList<Macro> Macros { get; }

    /// <summary>Loads macros.json into memory. Called once during startup.</summary>
    void Load();

    /// <summary>Case-insensitive lookup by macro name; null when not found.</summary>
    Macro Get(string name);

    /// <summary>Replaces the whole macro set (editor save) and persists it.</summary>
    void ReplaceAll(IEnumerable<Macro> macros);

    /// <summary>Persists the current in-memory macros to macros.json.</summary>
    void Save();

    /// <summary>
    /// Global hotkey (e.g. "Ctrl+Alt+Esc") that cancels all running macros; empty = disabled.
    /// Setting it persists macros.json and raises <see cref="MacrosChanged"/>.
    /// </summary>
    string StopHotkey { get; set; }

    /// <summary>
    /// True when <paramref name="name"/> is a usable macro name: non-empty, free of
    /// command-parser characters ( ) , &amp; and unique among all macros except
    /// <paramref name="ignore"/>.
    /// </summary>
    bool IsNameValid(string name, Macro ignore = null);

    /// <summary>
    /// Serializes an arbitrary subset of macros into a <c>macros.json</c>-shaped document,
    /// using the manager's own step converter. Exposed so callers that ship macros outside
    /// the config directory (profile packages, #133) do not have to re-declare the converter
    /// set — the step discriminator must never leak into config.json serialization.
    /// The global stop hotkey is deliberately never included; it is a user-level preference.
    /// </summary>
    string SerializeSubset(IEnumerable<Macro> macros);

    /// <summary>
    /// Counterpart of <see cref="SerializeSubset"/>: reads a <c>macros.json</c>-shaped document
    /// and returns its macros. Steps with unknown discriminators are dropped, exactly like
    /// <see cref="Load"/> does. Returns an empty list when the document cannot be parsed.
    /// </summary>
    IReadOnlyList<Macro> DeserializeSubset(string json);

    /// <summary>Raised after the macro set changed (editor save).</summary>
    event EventHandler MacrosChanged;
}
