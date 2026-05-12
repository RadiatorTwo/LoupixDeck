using System.Collections.Generic;

namespace LoupixDeck.Services.FolderNavigation;

/// <summary>
/// Convenience base for folder providers — handles the EntriesChanged event plumbing
/// and provides an empty default for rotary overrides.
/// </summary>
public abstract class FolderProviderBase : IFolderProvider
{
    private static readonly IReadOnlyDictionary<int, RotaryOverride> EmptyOverrides =
        new Dictionary<int, RotaryOverride>();

    public abstract string Title { get; }

    public abstract IReadOnlyList<FolderEntry> BuildEntries();

    public virtual IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => EmptyOverrides;

    public virtual void OnEnter() { }
    public virtual void OnExit() { }

    public event Action EntriesChanged;

    protected void RaiseEntriesChanged() => EntriesChanged?.Invoke();
}
