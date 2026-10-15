using System.Collections.Immutable;
using LoupixDeck.Registry;
using LoupixDeck.Services.Diagnostics;

namespace LoupixDeck.Services.FolderNavigation;

public sealed class FolderNavigationService(DeviceGeometry geometry) : IFolderNavigationService
{
    private readonly Stack<IFolderProvider> _stack = new();

    public FolderGrid Grid { get; } = FolderGrid.From(geometry);

    public bool IsActive => _stack.Count > 0;

    public IFolderProvider CurrentProvider { get; private set; }

    /// <summary>
    /// The current provider's entries. A provider's change announcement only marks them stale; they
    /// are rebuilt by the next read, which is the controller's coalesced redraw (or a press), so a
    /// burst of announcements costs one build instead of one per announcement.
    /// </summary>
    public ImmutableDictionary<int, FolderEntry> CurrentEntries
    {
        get
        {
            if (Volatile.Read(ref _entriesStale) != 0)
                RebuildStaleEntries();
            return _currentEntries;
        }
    }

    private ImmutableDictionary<int, FolderEntry> _currentEntries = ImmutableDictionary<int, FolderEntry>.Empty;

    // 1 once the current provider announced a change its entries do not show yet.
    private int _entriesStale;

    // Serialises entry builds: a stale read may run on any thread, alongside navigation.
    private readonly Lock _buildLock = new();

    public event Action StateChanged;

    public Task OpenFolder(IFolderProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // Subscribed after OnEnter: the entries are built right below, so a change the provider announces while
        // it is entering would only make the host redraw the page or parent folder, racing the redraw that
        // shows the new folder and leaving the old icons on screen.
        provider.OnEnter();

        // Only the folder on screen may trigger a redraw. A covered parent keeps running (it is not exited,
        // so its lifecycle stays as plugins expect), but its announcements would rebuild and repaint the
        // sub-folder on top of it. NavigateBack subscribes it again and rebuilds it with fresh entries.
        if (_stack.Count > 0)
            _stack.Peek().EntriesChanged -= OnProviderEntriesChanged;
        provider.EntriesChanged += OnProviderEntriesChanged;

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
            ClearActive();
        }
        else
        {
            // Subscribed before the rebuild, so a change announced while it runs is not lost.
            IFolderProvider parent = _stack.Peek();
            parent.EntriesChanged += OnProviderEntriesChanged;
            SetActive(parent);
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

        ClearActive();

        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private void OnProviderEntriesChanged()
    {
        FolderRedrawProbe.CountEvent();
        Volatile.Write(ref _entriesStale, 1);
        StateChanged?.Invoke();
    }

    private void RebuildStaleEntries()
    {
        lock (_buildLock)
        {
            // Cleared before the build, so an announcement made while it runs is built next time.
            if (Interlocked.Exchange(ref _entriesStale, 0) == 0) return;

            IFolderProvider provider = CurrentProvider;
            if (provider == null) return;

            long probe = FolderRedrawProbe.Begin();
            BuildActive(provider);
            FolderRedrawProbe.End("build entries", probe);
        }
    }

    private void ClearActive()
    {
        lock (_buildLock)
        {
            Volatile.Write(ref _entriesStale, 0);
            CurrentProvider = null;
            _currentEntries = ImmutableDictionary<int, FolderEntry>.Empty;
        }
    }

    private void SetActive(IFolderProvider provider)
    {
        lock (_buildLock)
        {
            Volatile.Write(ref _entriesStale, 0);
            CurrentProvider = provider;
            BuildActive(provider);
        }
    }

    private void BuildActive(IFolderProvider provider)
    {
        var entries = provider.BuildEntries() ?? Array.Empty<FolderEntry>();
        var dict = ImmutableDictionary.CreateBuilder<int, FolderEntry>();
        foreach (var entry in entries)
        {
            // Reserved, and out-of-grid entries would land on a side strip or throw.
            if (entry.SlotIndex == Grid.BackSlotIndex) continue;
            if (!Grid.IsGridSlot(entry.SlotIndex)) continue;
            dict[entry.SlotIndex] = entry;
        }
        _currentEntries = dict.ToImmutable();
    }
}

/// <summary>
/// Legacy 5x3 folder constants. Superseded by <see cref="FolderGrid"/>, which is derived from
/// the active device. Kept only for the built-in stress-test provider, which targets a 5x3
/// device on purpose. Do not use in new code.
/// </summary>
public static class FolderConstants
{
    /// <summary>5x3 grid: bottom-left = row 2, col 0 = index 10.</summary>
    public const int BackSlotIndex = 10;

    public const int TotalSlots = 15;
    public const int Columns = 5;
}
