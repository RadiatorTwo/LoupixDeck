using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

public class ElgatoDevices
{
    public event EventHandler<KeyLight> KeyLightAdded;
    public event EventHandler<KeyLight> KeyLightRemoved;
    public List<KeyLight> KeyLights { get; } = [];
    
    public void AddKeyLight(KeyLight keyLight)
    {
        KeyLights.Add(keyLight);
        SaveToFile();
        KeyLightAdded?.Invoke(this, keyLight);
    }
    
    public void RemoveKeyLight(KeyLight keyLight)
    {
        KeyLights.Remove(keyLight);
        SaveToFile();
        KeyLightRemoved?.Invoke(this, keyLight);
    }
    
    public static ElgatoDevices LoadFromFile()
    {
        var filePath = FileDialogHelper.GetConfigPath("elgato.json");

        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);

        var instance = JsonConvert.DeserializeObject<ElgatoDevices>(json);

        return instance;
    }
    
    public void SaveToFile()
    {
        var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };

        var json = JsonConvert.SerializeObject(this, settings);
        var filePath = FileDialogHelper.GetConfigPath("elgato.json");
        File.WriteAllText(filePath, json);
    }
}