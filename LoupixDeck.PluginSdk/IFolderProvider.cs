namespace LoupixDeck.PluginSdk;

/// <summary>
/// Supplies the content of one folder in the touch-screen folder navigation
/// mode. A plugin opens a folder via <see cref="IPluginHost.OpenFolder"/>.
/// Implementations may be stateful — a sub-folder typically captures the
/// parent's selection.
/// </summary>
public interface IFolderProvider
{
    /// <summary>Folder title shown by the host.</summary>
    string Title { get; }

    /// <summary>Builds the current slot content.</summary>
    IReadOnlyList<FolderEntry> BuildEntries();

    /// <summary>Per-rotary-encoder overrides while this folder is open,
    /// keyed by rotary index (0 = first encoder).</summary>
    IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides { get; }

    /// <summary>Called once when the folder is pushed — wire up listeners here.</summary>
    void OnEnter();

    /// <summary>Called once when the folder is popped — detach listeners here.</summary>
    void OnExit();

    /// <summary>Raised when the entries (or their displayed data) changed and
    /// the folder view must be redrawn.</summary>
    event Action EntriesChanged;
}
