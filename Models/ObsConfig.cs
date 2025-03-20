using Newtonsoft.Json;

namespace LoupixDeck.Models;

public class ObsConfig
{
    public string Url { get; set; }
    public string Password { get; set; }

    // Lädt die Konfiguration aus einer JSON-Datei
    public static ObsConfig LoadConfig()
    {
        var filePath = Utils.FileDialogHelper.GetConfigPath("obsconfig.json");
        if (!File.Exists(filePath))
        {
            // Falls die Datei nicht existiert, werden Default-Werte verwendet.
            return new ObsConfig
            {
                Url = "ws://127.0.0.1:4455",
                Password = ""
            };
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<ObsConfig>(json);
            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            // Bei Fehlern werden ebenfalls Default-Werte zurückgegeben.
            return new ObsConfig
            {
                Url = "ws://127.0.0.1:4455",
                Password = ""
            };
        }
    }

    // Speichert die Konfiguration als JSON-Datei
    public void SaveConfig()
    {
        try
        {
            var filePath = Utils.FileDialogHelper.GetConfigPath("obsconfig.json");
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
            Console.WriteLine("Configuration saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}