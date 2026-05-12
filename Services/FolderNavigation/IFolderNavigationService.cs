namespace LoupixDeck.Services.FolderNavigation;

public interface IFolderNavigationService
{
    bool IsActive { get; }

    /// <summary>The provider on top of the navigation stack, or null when not active.</summary>
    IFolderProvider CurrentProvider { get; }

    /// <summary>Cached entries of the current provider, keyed by SlotIndex for fast touch dispatch.</summary>
    IReadOnlyDictionary<int, FolderEntry> CurrentEntries { get; }

    /// <summary>Pushes a new folder onto the stack.</summary>
    Task OpenFolder(IFolderProvider provider);

    /// <summary>Pops the top folder. When the stack becomes empty, exits folder mode.</summary>
    Task NavigateBack();

    /// <summary>
    /// Fired when state changes: open, back, or a provider raised EntriesChanged.
    /// The controller subscribes and redraws the touch buttons.
    /// </summary>
    event Action StateChanged;
}
