using LoupixDeck.PluginSdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// File-backed <see cref="IPluginSettings"/>. Each plugin gets its own store at
/// <c>plugins/&lt;id&gt;/settings.json</c>, isolated from the core config.
/// </summary>
public sealed class PluginSettingsStore : IPluginSettings
{
    private readonly string _path;
    private readonly object _gate = new();
    private JObject _data;

    public PluginSettingsStore(string settingsFilePath)
    {
        _path = settingsFilePath;
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _data = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginSettingsStore: failed to read '{_path}': {ex.Message}");
        }

        _data = new JObject();
    }

    public T Get<T>(string key, T defaultValue = default)
    {
        lock (_gate)
        {
            var token = _data[key];
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                return token.ToObject<T>();
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _data[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }
    }

    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _data[key] != null;
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _data.Remove(key);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json;
            lock (_gate)
            {
                json = _data.ToString(Formatting.Indented);
            }

            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginSettingsStore: failed to write '{_path}': {ex.Message}");
        }
    }
}
