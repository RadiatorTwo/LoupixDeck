namespace LoupixDeck.Models.Macros;

/// <summary>
/// Root document of macros.json. Kept separate from config.json so user macros
/// survive independently of device configs. Schema changes should stay additive;
/// unknown step types in newer files are skipped gracefully on load.
/// </summary>
public class MacroSettings
{
    // v2 (#136): per-macro ExecutionMode plus control-flow / variable / wait / prompt steps.
    // All additive — the loader stays version-tolerant and reads older files unchanged.
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public List<Macro> Macros { get; set; } = [];

    /// <summary>
    /// Optional global hotkey (e.g. "Ctrl+Alt+Esc") that cancels all running macros.
    /// Empty disables the hotkey. Additive — older files default to disabled.
    /// </summary>
    public string StopHotkey { get; set; } = string.Empty;
}
