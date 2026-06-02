namespace LoupixDeck.Models.Macros;

/// <summary>
/// Root document of macros.json. Kept separate from config.json so user macros
/// survive independently of device configs. Schema changes should stay additive;
/// unknown step types in newer files are skipped gracefully on load.
/// </summary>
public class MacroSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<Macro> Macros { get; set; } = [];
}
