using LoupixDeck.Models.Companion;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.Companion;

/// <inheritdoc cref="ICompanionStore"/>
public sealed class CompanionStore : ICompanionStore
{
    public const string FileName = "companions.json";

    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly string _path;

    public CompanionStore() : this(FileDialogHelper.GetConfigPath(FileName))
    {
    }

    public CompanionStore(string path)
    {
        _path = path;
    }

    public CompanionConfig Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new CompanionConfig();

            string json = File.ReadAllText(_path);
            CompanionConfig config = JsonConvert.DeserializeObject<CompanionConfig>(json, Settings) ?? new CompanionConfig();

            config.Groups ??= [];

            if (config.Version > CompanionConfig.CurrentVersion)
                Console.WriteLine($"[Companions] {_path} was written by a newer version (v{config.Version}); loading what this build understands.");

            if (CompanionGroupValidator.Heal(config.Groups))
                Console.WriteLine($"[Companions] {_path} contained invalid group assignments and was healed.");

            return config;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[Companions] Failed to read {_path}: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] {_path} is unreadable ({ex.GetType().Name}: {ex.Message}) — backing it up and starting empty.");
            BackupCorrupted();
            return new CompanionConfig();
        }
    }

    public void Save(CompanionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Version = CompanionConfig.CurrentVersion;

        string json = JsonConvert.SerializeObject(config, Settings);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(_path))
            File.Delete(_path);
        File.Move(temp, _path);
    }

    private void BackupCorrupted()
    {
        try
        {
            if (!File.Exists(_path)) return;
            string backup = $"{_path}.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            File.Move(_path, backup);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Failed to back up {_path}: {ex.Message}");
        }
    }
}
