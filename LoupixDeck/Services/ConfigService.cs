using Newtonsoft.Json;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services.Migrations;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

public interface IConfigService
{
    T LoadConfig<T>(string filePath) where T : class;
    void SaveConfig(object config, string filePath);
}

public class ConfigService : IConfigService
{
    private readonly JsonSerializerSettings _settings;

    /// <summary>
    /// Config upgrade chain, ordered implicitly by <see cref="IConfigMigration.FromVersion"/>.
    /// </summary>
    private readonly List<IConfigMigration> _migrations =
    [
        new PluginConfigMigrator(),
        new RotaryPageSideSplitMigrator(),
        new WallpaperAssetMigrator(),
        new WallpaperSlotMigrator()
    ];

    public ConfigService()
    {
        _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };
        _settings.Converters.Add(new ColorJsonConverter());
        _settings.Converters.Add(new SKBitmapBase64Converter());
        _settings.Converters.Add(new LayerJsonConverter());
    }

    private enum ConfigVersionState
    {
        /// <summary>Version matches the current schema — load as-is.</summary>
        Current,

        /// <summary>Older schema — run the migration chain before loading.</summary>
        NeedsMigration,

        /// <summary>Newer schema than this build understands — discard.</summary>
        FromFuture
    }

    public T LoadConfig<T>(string filePath) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);

            if (typeof(T) == typeof(LoupedeckConfig))
            {
                var migrated = LoadVersionedConfig<T>(json, filePath);
                // A null result means "backed up, start fresh"; a non-null
                // result is the (possibly migrated) config.
                return migrated;
            }

            return JsonConvert.DeserializeObject<T>(json, _settings);
        }
        catch (IOException ex)
        {
            // I/O errors are temporary issues, not corruption - rethrow
            Console.WriteLine($"Failed to read config from {filePath}: {ex.Message}");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            // Permission issues are temporary, not corruption - rethrow
            Console.WriteLine($"Access denied reading config from {filePath}: {ex.Message}");
            throw;
        }
        catch (Exception ex) when (ex is JsonException or
                                         InvalidDataException or
                                         FormatException or
                                         InvalidOperationException)
        {
            // Data corruption exceptions - backup the file
            Console.WriteLine($"Config file corrupted at {filePath}: {ex.GetType().Name} - {ex.Message}");

            BackupCorruptedFile(filePath);

            // Return null to allow application to create new default config
            return null;
        }
        catch (Exception ex)
        {
            // Unexpected exceptions - backup the file as a precaution
            Console.WriteLine($"Unexpected error loading config from {filePath}: {ex.GetType().Name} - {ex.Message}");

            BackupCorruptedFile(filePath);

            // Return null to allow application to create new default config
            return null;
        }
    }

    /// <summary>
    /// Loads a <see cref="LoupedeckConfig"/>, classifying its schema version and
    /// running the migration chain when the file predates the current schema.
    /// Returns null when the file must be discarded (backed up) and a fresh
    /// config created. Parse errors propagate to the caller's corruption handler.
    /// </summary>
    private T LoadVersionedConfig<T>(string json, string filePath) where T : class
    {
        var root = JObject.Parse(json);

        switch (ClassifyVersion(root))
        {
            case ConfigVersionState.FromFuture:
                Console.WriteLine($"Config {filePath} is from a newer version — backing up and starting fresh.");
                BackupCorruptedFile(filePath);
                return null;

            case ConfigVersionState.NeedsMigration:
                var upgraded = MigrateIfNeeded(root, filePath);
                if (upgraded == null)
                {
                    Console.WriteLine($"Config {filePath} could not be migrated — backing up and starting fresh.");
                    BackupCorruptedFile(filePath);
                    return null;
                }

                var result = upgraded.ToObject<T>(JsonSerializer.Create(_settings));

                // Persist the upgrade so it is durable and the migration runs once.
                try
                {
                    SaveConfig(result, filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to persist migrated config {filePath}: {ex.Message}");
                }

                return result;

            default:
                return JsonConvert.DeserializeObject<T>(json, _settings);
        }
    }

    private static int GetVersion(JObject root)
    {
        return root["Version"]?.Value<int?>() ?? root["version"]?.Value<int?>() ?? 1;
    }

    private static ConfigVersionState ClassifyVersion(JObject root)
    {
        var version = GetVersion(root);

        if (version == LoupedeckConfig.CurrentVersion)
            return ConfigVersionState.Current;

        return version < LoupedeckConfig.CurrentVersion
            ? ConfigVersionState.NeedsMigration
            : ConfigVersionState.FromFuture;
    }

    /// <summary>
    /// Applies the migration chain step by step until the config reaches the
    /// current version. Returns null when a required migration step is missing
    /// or fails to advance the version.
    /// </summary>
    private JObject MigrateIfNeeded(JObject root, string filePath)
    {
        var version = GetVersion(root);

        while (version < LoupedeckConfig.CurrentVersion)
        {
            var migration = _migrations.FirstOrDefault(m => m.FromVersion == version);
            if (migration == null)
            {
                Console.WriteLine($"No migration registered from config version {version}.");
                return null;
            }

            try
            {
                migration.Apply(root, filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Migration from config version {version} threw: {ex.Message}");
                return null;
            }

            var newVersion = GetVersion(root);
            if (newVersion <= version)
            {
                Console.WriteLine($"Migration from config version {version} did not advance the version.");
                return null;
            }

            Console.WriteLine($"Migrated config {filePath}: v{version} → v{newVersion}.");
            version = newVersion;
        }

        return root;
    }

    private static void BackupCorruptedFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = $"{filePath}.corrupted.{timestamp}.bak";
            File.Move(filePath, backupPath);
            Console.WriteLine($"Corrupted config backed up to: {backupPath}");
        }
        catch (Exception backupEx)
        {
            Console.WriteLine($"Failed to backup corrupted config: {backupEx.Message}");
        }
    }

    public void SaveConfig(object config, string filePath)
    {
        try
        {
            var json = JsonConvert.SerializeObject(config, _settings);

            // Atomic write: write to temp file first, then rename
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Replace old file with new one
            if (File.Exists(filePath))
                File.Delete(filePath);

            File.Move(tempPath, filePath);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Failed to save config to {filePath}: {ex.Message}");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied saving config to {filePath}: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error saving config to {filePath}: {ex.Message}");
            throw;
        }
    }
}