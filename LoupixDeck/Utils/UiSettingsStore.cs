using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoupixDeck.Utils;

/// <summary>
/// Reads and writes the global, device-independent <c>ui-settings.json</c> next to the device
/// configs. Every value is its own top-level key and a write merges into the file instead of
/// replacing it, so separate features (language, update check, ...) never erase each other's
/// values and a file written by an older version keeps loading. A static helper rather than a DI
/// service, because the language is read before the container exists.
/// </summary>
public static class UiSettingsStore
{
    private const string FileName = "ui-settings.json";

    private static readonly Lock Gate = new();

    private static string FilePath => Path.Combine(FileDialogHelper.GetConfigDir(), FileName);

    public static string GetString(string key)
    {
        return Read(key) is JsonValue value && value.TryGetValue(out string text) ? text : null;
    }

    public static bool GetBool(string key, bool defaultValue)
    {
        return Read(key) is JsonValue value && value.TryGetValue(out bool flag) ? flag : defaultValue;
    }

    public static void Set(string key, string value)
    {
        Write(key, value is null ? null : JsonValue.Create(value));
    }

    public static void Set(string key, bool value)
    {
        Write(key, JsonValue.Create(value));
    }

    private static JsonNode Read(string key)
    {
        lock (Gate)
        {
            return Load()?[key];
        }
    }

    private static void Write(string key, JsonNode value)
    {
        lock (Gate)
        {
            string path = FilePath;
            try
            {
                JsonObject root = Load() ?? new JsonObject();
                root[key] = value;
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UiSettings] Failed to write '{path}': {ex.Message}");
            }
        }
    }

    private static JsonObject Load()
    {
        string path = FilePath;
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UiSettings] Failed to read '{path}': {ex.Message}");
            return null;
        }
    }
}
