using Newtonsoft.Json;
using LoupixDeck.Models.Converter;

namespace LoupixDeck.Services;

public interface IConfigService
{
    T LoadConfig<T>(string filePath) where T : class;
    void SaveConfig(object config, string filePath);
}

public class ConfigService : IConfigService
{
    private readonly JsonSerializerSettings _settings;

    public ConfigService()
    {
        _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };
        _settings.Converters.Add(new ColorJsonConverter());
        _settings.Converters.Add(new SKBitmapBase64Converter());
    }

    public T LoadConfig<T>(string filePath) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
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

    private void BackupCorruptedFile(string filePath)
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