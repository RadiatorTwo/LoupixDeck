using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.PluginStore;

/// <summary>
/// Remembers which plugin contributed which command name (<c>plugin-commands.json</c> next to the device
/// configs). Plugin command names carry no plugin id, so this is how a button that uses a command of a
/// removed or not yet installed plugin is recognised as such instead of being taken for a shell command.
/// </summary>
public interface IPluginCommandIndex
{
    /// <summary>Records the commands a plugin provides; persisted when something changed.</summary>
    void Record(string pluginId, IEnumerable<string> commandNames);

    /// <summary>The id of the plugin that provided <paramref name="commandName"/> at some point, or null.</summary>
    string FindPluginId(string commandName);
}

/// <inheritdoc cref="IPluginCommandIndex"/>
public sealed class PluginCommandIndex : IPluginCommandIndex
{
    private const string FileName = "plugin-commands.json";

    private readonly object _lock = new();
    private Dictionary<string, string> _owners;

    private static string FilePath => Path.Combine(FileDialogHelper.GetConfigDir(), FileName);

    public void Record(string pluginId, IEnumerable<string> commandNames)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || commandNames is null)
        {
            return;
        }

        lock (_lock)
        {
            Dictionary<string, string> owners = Load();
            bool changed = false;
            foreach (string name in commandNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                if (!owners.TryGetValue(name, out string existing) || existing != pluginId)
                {
                    owners[name] = pluginId;
                    changed = true;
                }
            }

            if (changed)
            {
                Save(owners);
            }
        }
    }

    public string FindPluginId(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return null;
        }

        lock (_lock)
        {
            return Load().GetValueOrDefault(commandName);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_owners is not null)
        {
            return _owners;
        }

        try
        {
            _owners = File.Exists(FilePath)
                ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(FilePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[PluginStore] Could not read {FileName}: {ex.Message}");
        }

        _owners = _owners is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(_owners, StringComparer.Ordinal);
        return _owners;
    }

    private static void Save(Dictionary<string, string> owners)
    {
        try
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(owners, Formatting.Indented));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[PluginStore] Could not write {FileName}: {ex.Message}");
        }
    }
}
