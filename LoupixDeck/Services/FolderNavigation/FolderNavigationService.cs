using System.Collections.Immutable;

namespace LoupixDeck.Services.FolderNavigation;

public sealed class FolderNavigationService : IFolderNavigationService
{
    private readonly Stack<IFolderProvider> _stack = new();

    public bool IsActive => _stack.Count > 0;

    public IFolderProvider CurrentProvider { get; private set; }

    public ImmutableDictionary<int, FolderEntry> CurrentEntries { get; private set; } = ImmutableDictionary<int, FolderEntry>.Empty;

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
            CurrentProvider = null;
            CurrentEntries = ImmutableDictionary<int, FolderEntry>.Empty;
        }
        else
        {
            SetActive(_stack.Peek());
        }

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task ExitAll()
    {
        if (_stack.Count == 0)
            return Task.CompletedTask;

        // Unsubscribe + OnExit every frame so no provider keeps a live reference.
        while (_stack.Count > 0)
        {
            var leaving = _stack.Pop();
            leaving.EntriesChanged -= OnProviderEntriesChanged;
            try { leaving.OnExit(); } catch { /* swallow */ }
        }

        CurrentProvider = null;
        CurrentEntries = ImmutableDictionary<int, FolderEntry>.Empty;

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private void OnProviderEntriesChanged()
    {
        if (CurrentProvider != null)
            SetActive(CurrentProvider);

        StateChanged?.Invoke();
    }

    private void SetActive(IFolderProvider provider)
    {
        CurrentProvider = provider;
        var entries = provider.BuildEntries() ?? Array.Empty<FolderEntry>();
        var dict = ImmutableDictionary.CreateBuilder<int, FolderEntry>();
        foreach (var entry in entries)
        {
            // Skip the back-button slot — it's reserved.
            if (entry.SlotIndex == FolderConstants.BackSlotIndex) continue;
            dict[entry.SlotIndex] = entry;
        }
        CurrentEntries = dict.ToImmutable();
    }
}

public static class FolderConstants
{
    /// <summary>5x3 grid: bottom-left = row 2, col 0 = index 10.</summary>
    public const int BackSlotIndex = 10;

    public const int TotalSlots = 15;
    public const int Columns = 5;
}
