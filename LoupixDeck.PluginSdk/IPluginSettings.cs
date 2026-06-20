namespace LoupixDeck.PluginSdk;

/// <summary>
/// Per-plugin isolated key/value configuration store. The host backs this with
/// <c>plugins/&lt;plugin-id&gt;/settings.json</c>, so plugins never write into
/// the core's <c>config.json</c>.
/// </summary>
public interface IPluginSettings
{
    /// <summary>Reads a value, returning <paramref name="defaultValue"/> when the
    /// key is absent.</summary>
    T? Get<T>(string key, T? defaultValue = default);

    /// <summary>Stores a value. Call <see cref="Save"/> to persist.</summary>
    void Set<T>(string key, T value);

    bool Contains(string key);

    void Remove(string key);

    /// <summary>All keys currently present in the store, in undefined order.</summary>
    IEnumerable<string> Keys { get; }

    /// <summary>Persists all pending changes to disk.</summary>
    void Save();
}
