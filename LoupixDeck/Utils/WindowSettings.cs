using System.Text.Json;
using LoupixDeck.Models;

namespace LoupixDeck.Utils;

/// <summary>
/// Settings of the one main window, which apply to the whole app and therefore live in the
/// device-independent <c>ui-settings.json</c>. They used to be kept in each device's config, where
/// the start-up read the primary device's value. Which device is primary depends on what has
/// enumerated by then, so a Windows-login start with a second device appearing first opened the
/// window although "Start minimized to tray" was on for the other one (issue #567).
/// </summary>
public static class WindowSettings
{
    private const string StartMinimizedKey = "StartMinimizedToTray";
    private const string CloseButtonKey = "CloseButtonBehavior";

    /// <summary>Start hidden, with only the tray icon.</summary>
    public static bool StartMinimizedToTray
    {
        get
        {
            if (!UiSettingsStore.Contains(StartMinimizedKey))
                UiSettingsStore.Set(StartMinimizedKey, AnyDeviceConfigHas(StartMinimizedKey, v => v.ValueKind == JsonValueKind.True));
            return UiSettingsStore.GetBool(StartMinimizedKey, false);
        }
        set => UiSettingsStore.Set(StartMinimizedKey, value);
    }

    /// <summary>What the close (X) button does.</summary>
    public static CloseButtonBehavior CloseButtonBehavior
    {
        get
        {
            if (!UiSettingsStore.Contains(CloseButtonKey))
            {
                // Quit is the deliberate choice (the default leaves the app unreachable without a
                // tray), so it wins when any device had it.
                bool quit = AnyDeviceConfigHas(CloseButtonKey, IsQuit);
                UiSettingsStore.Set(CloseButtonKey, (quit ? CloseButtonBehavior.Quit : CloseButtonBehavior.MinimizeToTray).ToString());
            }
            return Enum.TryParse(UiSettingsStore.GetString(CloseButtonKey), out CloseButtonBehavior behavior)
                ? behavior
                : CloseButtonBehavior.MinimizeToTray;
        }
        set => UiSettingsStore.Set(CloseButtonKey, value.ToString());
    }

    // Device configs store the enum as its number; accept the name as well.
    private static bool IsQuit(JsonElement value) =>
        (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number == (int)CloseButtonBehavior.Quit)
        || (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), nameof(CloseButtonBehavior.Quit), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Migration from the per-device field: true when any device config on disk, connected or
    /// not, has a <paramref name="key"/> value that matches <paramref name="match"/>. Reads the files rather than
    /// the loaded configs because at start-up only the devices that have enumerated are loaded.
    /// </summary>
    private static bool AnyDeviceConfigHas(string key, Func<JsonElement, bool> match)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(FileDialogHelper.GetConfigDir(), "config*.json"))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty(key, out JsonElement value)
                        && match(value))
                        return true;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"[WindowSettings] Skipped '{path}' while migrating '{key}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[WindowSettings] Could not read the device configs: {ex.Message}");
        }
        return false;
    }
}
