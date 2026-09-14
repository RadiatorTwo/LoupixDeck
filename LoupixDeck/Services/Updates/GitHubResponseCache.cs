using System.Text.Json;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Updates;

/// <summary>
/// The last GitHub API response per URL with its <c>ETag</c> (<c>github-cache.json</c> next to the device
/// configs). A request that sends the ETag back and gets <c>304 Not Modified</c> does not count against
/// GitHub's hourly limit for unauthenticated clients, so repeated update checks and store refreshes stay
/// free as long as nothing was released. Kept on disk so app restarts benefit too.
/// </summary>
public static class GitHubResponseCache
{
    private const string FileName = "github-cache.json";

    private static readonly Lock Gate = new();
    private static Dictionary<string, Entry> _entries;

    private static string FilePath => Path.Combine(FileDialogHelper.GetConfigDir(), FileName);

    public static Entry Get(string url)
    {
        lock (Gate)
        {
            return Load().GetValueOrDefault(url);
        }
    }

    public static void Set(string url, string etag, string body)
    {
        if (string.IsNullOrEmpty(etag))
        {
            return;
        }

        lock (Gate)
        {
            Dictionary<string, Entry> entries = Load();
            entries[url] = new Entry(etag, body);
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[GitHub] Could not write {FileName}: {ex.Message}");
            }
        }
    }

    private static Dictionary<string, Entry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            if (File.Exists(FilePath))
            {
                _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[GitHub] Could not read {FileName}: {ex.Message}");
        }

        _entries ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
        return _entries;
    }

    public sealed record Entry(string ETag, string Body);
}
