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
    /// True when <paramref name="name"/> is a usable macro name: non-empty, free of
    /// command-parser characters ( ) , &amp; and unique among all macros except
    /// <paramref name="ignore"/>.
    /// </summary>
    bool IsNameValid(string name, Macro ignore = null);

    /// <summary>Raised after the macro set changed (editor save).</summary>
    event EventHandler MacrosChanged;
}
