namespace LoupixDeck.Services.FolderNavigation;

public sealed class FolderNavigationService : IFolderNavigationService
{
    private readonly Stack<IFolderProvider> _stack = new();
    private Dictionary<int, FolderEntry> _currentEntries = new();
    private IFolderProvider _activeProvider;

    public bool IsActive => _stack.Count > 0;

    public IFolderProvider CurrentProvider => _activeProvider;

    public IReadOnlyDictionary<int, FolderEntry> CurrentEntries => _currentEntries;

    public event Action StateChanged;

    public Task OpenFolder(IFolderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        provider.EntriesChanged += OnProviderEntriesChanged;
        provider.OnEnter();

        _stack.Push(provider);
        SetActive(provider);

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task NavigateBack()
    {
        if (_stack.Count == 0)
            return Task.CompletedTask;

        var leaving = _stack.Pop();
        leaving.EntriesChanged -= OnProviderEntriesChanged;
        try { leaving.OnExit(); } catch { /* swallow */ }

        if (_stack.Count == 0)
        {
            _activeProvider = null;
            _currentEntries = new Dictionary<int, FolderEntry>();
        }
        else
        {
            SetActive(_stack.Peek());
        }

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private void OnProviderEntriesChanged()
    {
        if (_activeProvider != null)
            SetActive(_activeProvider);

        StateChanged?.Invoke();
    }

    private void SetActive(IFolderProvider provider)
    {
        _activeProvider = provider;
        var entries = provider.BuildEntries() ?? Array.Empty<FolderEntry>();
        var dict = new Dictionary<int, FolderEntry>(entries.Count);
        foreach (var entry in entries)
        {
            // Skip the back-button slot — it's reserved.
            if (entry.SlotIndex == FolderConstants.BackSlotIndex) continue;
            dict[entry.SlotIndex] = entry;
        }
        _currentEntries = dict;
    }
}

public static class FolderConstants
{
    /// <summary>5x3 grid: bottom-left = row 2, col 0 = index 10.</summary>
    public const int BackSlotIndex = 10;

    public const int TotalSlots = 15;
    public const int Columns = 5;
}
