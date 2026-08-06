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

    public ExclusiveControlScope ActiveScope
    {
        get
        {
            IExclusiveModeProvider provider = _current;
            if (provider == null)
                return ExclusiveControlScope.None;

            // Read fresh every time — a provider may narrow or widen its override while
            // running. A throwing getter must not silently un-guard the device, so fall
            // back to the pre-#127 behaviour (whole-device takeover).
            try { return provider.Scope; }
            catch (Exception ex)
            {
                Console.WriteLine($"ExclusiveMode.Scope threw: {ex.Message}");
                return ExclusiveControlScope.All;
            }
        }
    }

    public bool Owns(ExclusiveControlScope scope)
    {
        if (scope == ExclusiveControlScope.None)
            return false;

        return (ActiveScope & scope) == scope;
    }

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
