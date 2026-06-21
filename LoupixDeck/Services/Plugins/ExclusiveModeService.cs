using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <inheritdoc cref="IExclusiveModeService"/>
public sealed class ExclusiveModeService : IExclusiveModeService
{
    // Single-owner state. Lock guards the transition; the actual rendering /
    // input-routing happens on the caller's thread (controller / UDP worker)
    // and reads volatile state.
    private readonly Lock _gate = new();
    private IExclusiveModeProvider _current;

    public bool IsActive => _current != null;

    public IExclusiveModeProvider Current => _current;

    public event Action StateChanged;

    public bool TryEnter(IExclusiveModeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_gate)
        {
            if (_current != null)
                return false;

            _current = provider;
            provider.EntriesChanged += OnProviderEntriesChanged;
        }

        try { provider.OnEnter(); }
        catch (Exception ex) { Console.WriteLine($"ExclusiveMode.OnEnter threw: {ex.Message}"); }

        StateChanged?.Invoke();
        return true;
    }

    public void Exit(IExclusiveModeProvider provider)
    {
        IExclusiveModeProvider leaving = null;
        lock (_gate)
        {
            if (_current == null || !ReferenceEquals(_current, provider))
                return;

            leaving = _current;
            _current = null;
        }

        leaving.EntriesChanged -= OnProviderEntriesChanged;
        try { leaving.OnExit(); }
        catch (Exception ex) { Console.WriteLine($"ExclusiveMode.OnExit threw: {ex.Message}"); }

        StateChanged?.Invoke();
    }

    private void OnProviderEntriesChanged(object sender, EventArgs e) => StateChanged?.Invoke();
}
