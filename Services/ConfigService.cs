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
        _settings.Converters.Add(new BitmapJsonConverter());
    }

    public T LoadConfig<T>(string filePath) where T : class
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = File.ReadAllText(filePath);
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }

    public void SaveConfig(object config, string filePath)
    {
        var json = JsonConvert.SerializeObject(config, _settings);
        File.WriteAllText(filePath, json);
    }
}