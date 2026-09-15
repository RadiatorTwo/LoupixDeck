using LoupixDeck.Registry;
using Newtonsoft.Json;

namespace LoupixDeck.Utils;

/// <summary>
/// Decides which config file belongs to a physical device.
/// <para>
/// A device's config is <c>config_&lt;slug&gt;.json</c>. Only a second unit of the same model gets a
/// file with its serial, <c>config_&lt;slug&gt;_&lt;serial&gt;.json</c>, so a single device never has a
/// serial in its file name. Files are never renamed:
/// </para>
/// <list type="number">
/// <item>A file with the device's serial that already exists keeps being used (configs scoped by
/// earlier builds stay where they are).</item>
/// <item>Otherwise the slug-only file is used unless it belongs to another unit. Ownership is the
/// <c>DeviceSerial</c> stamped into the file; a file without a stamp belongs to the first unit that
/// asks for it in this process.</item>
/// <item>Otherwise the device gets the file with its serial.</item>
/// </list>
/// Decisions are remembered per process, so every caller gets the same path for the same device.
/// </summary>
public static class DeviceConfigPath
{
    private static readonly Lock Gate = new();

    // Slug-only path → serial of the unit that uses it in this process.
    private static readonly Dictionary<string, string> Claims = new(StringComparer.OrdinalIgnoreCase);

    public static string Resolve(DeviceRegistry.DeviceInfo info, string serial)
    {
        ArgumentNullException.ThrowIfNull(info);

        string slugOnly = FileDialogHelper.GetConfigPath(info);
        string safe = SerialNormalizer.ForFilename(serial);
        if (string.IsNullOrEmpty(safe))
            return slugOnly;

        string scoped = Path.Combine(FileDialogHelper.GetConfigDir(), $"config_{info.Slug}_{safe}.json");

        lock (Gate)
        {
            if (Claims.TryGetValue(slugOnly, out string claimedBy))
                return SameSerial(claimedBy, serial) ? slugOnly : scoped;

            if (File.Exists(scoped))
                return scoped;

            string owner = File.Exists(slugOnly) ? ReadStampedSerial(slugOnly) : null;
            if (!string.IsNullOrEmpty(owner) && !SameSerial(owner, serial))
                return scoped;

            Claims[slugOnly] = serial;
            return slugOnly;
        }
    }

    /// <summary>The serial of the unit a config file belongs to, read from its <c>DeviceSerial</c>.
    /// Null when the file has none or cannot be read.</summary>
    public static string ReadStampedSerial(string path)
    {
        try
        {
            using StreamReader stream = File.OpenText(path);
            using JsonTextReader reader = new(stream);
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1 &&
                    string.Equals((string)reader.Value, "DeviceSerial", StringComparison.Ordinal))
                {
                    return reader.ReadAsString();
                }

                // Skip nested objects/arrays wholesale; DeviceSerial sits at the root.
                if (reader.Depth >= 1 && reader.TokenType is JsonToken.StartObject or JsonToken.StartArray)
                    reader.Skip();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeviceConfigPath] Could not read the device serial of '{Path.GetFileName(path)}': {ex.Message}");
        }

        return null;
    }

    private static bool SameSerial(string a, string b) =>
        string.Equals(SerialNormalizer.ForFilename(a), SerialNormalizer.ForFilename(b), StringComparison.Ordinal);
}
