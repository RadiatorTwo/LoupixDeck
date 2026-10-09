namespace LoupixDeck.Services.FolderNavigation;

/// <summary>
/// Supplies folder content for the folder navigation mode. Implementations may be
/// stateful (a sub-folder typically captures the parent's selection).
/// Rotary overrides are keyed by rotary index (0 = KNOB_TL, 1 = KNOB_CL).
/// </summary>
public interface IFolderProvider
{
    string Title { get; }

    IReadOnlyList<FolderEntry> BuildEntries();

    IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides { get; }

    /// <summary>
    /// Creates the session that drives one side strip while this folder is visible, or null when
    /// the folder leaves that strip to the host (blank, or the rotary override indicators).
    /// </summary>
    LoupixDeck.PluginSdk.ISideStripSession CreateSideStripSession(LoupixDeck.PluginSdk.SideStripContext context);

    /// <summary>Called once when the folder is pushed. Wire up any external listeners here.</summary>
    void OnEnter();

    /// <summary>Called once when the folder is popped. Detach listeners.</summary>
    void OnExit();

    /// <summary>
    /// Provider raises this when its entries (or the data they display) have changed and
    /// the folder view needs to be redrawn. The navigation service forwards it to the controller.
    /// </summary>
    event Action EntriesChanged;
}
