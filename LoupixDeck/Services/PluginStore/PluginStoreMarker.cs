using LoupixDeck.Services.Plugins;
using Newtonsoft.Json;

namespace LoupixDeck.Services.PluginStore;

/// <summary>
/// <c>store.json</c> inside a plugin folder: the plugin is managed by the store and gets its updates from
/// <see cref="Repository"/>. A plugin folder without it counts as manually installed.
/// </summary>
public sealed class PluginStoreMarker
{
    public string Repository { get; set; }

    /// <summary>Release tag the installed files came from; null for an adopted, formerly bundled plugin.</summary>
    public string Tag { get; set; }

    public DateTime InstalledAt { get; set; }

    public static PluginStoreMarker Read(string pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return null;
        }

        string path = Path.Combine(pluginDirectory, PluginInstaller.StoreMarkerFileName);
        try
        {
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<PluginStoreMarker>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[PluginStore] Could not read '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the marker; returns false (and logs) when the folder is not writable.</summary>
    public bool Write(string pluginDirectory)
    {
        string path = Path.Combine(pluginDirectory, PluginInstaller.StoreMarkerFileName);
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[PluginStore] Could not write '{path}': {ex.Message}");
            return false;
        }
    }
}
