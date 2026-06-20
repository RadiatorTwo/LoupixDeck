namespace LoupixDeck.PluginSdk;

/// <summary>
/// Convenience base for <see cref="IFolderProvider"/> implementations — handles
/// the <see cref="EntriesChanged"/> plumbing and supplies an empty default for
/// <see cref="RotaryOverrides"/>.
/// </summary>
public abstract class FolderProviderBase : IFolderProvider
{
    private static readonly IReadOnlyDictionary<int, RotaryOverride> EmptyOverrides =
        new Dictionary<int, RotaryOverride>();

    public abstract string Title { get; }

    public abstract IReadOnlyList<FolderEntry> BuildEntries();

    public virtual IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => EmptyOverrides;

    public virtual void OnEnter()
    {
    }

    public virtual void OnExit()
    {
    }

    public event Action? EntriesChanged;

    protected void RaiseEntriesChanged() => EntriesChanged?.Invoke();
}
