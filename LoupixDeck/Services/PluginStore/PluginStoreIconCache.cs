using Avalonia.Media.Imaging;
using LoupixDeck.Services.Updates;

namespace LoupixDeck.Services.PluginStore;

/// <summary>
/// The plugin icons loaded in this session, by URL (issue #330). The store rebuilds its rows on every
/// refresh, install, update and removal; without this each rebuild fetched every icon again.
/// </summary>
public static class PluginStoreIconCache
{
    /// <summary>Running and finished downloads. A failed one ends with null and is replaced on the next request.</summary>
    private static readonly Dictionary<string, Task<Bitmap>> Icons = new(StringComparer.Ordinal);

    /// <summary>
    /// The icon behind <paramref name="url"/>, or null when it could not be loaded. Requests for the same
    /// URL share one download; <paramref name="cancellation"/> only ends the caller's wait, not the download.
    /// </summary>
    public static Task<Bitmap> GetAsync(string url, CancellationToken cancellation)
    {
        Task<Bitmap> icon;
        lock (Icons)
        {
            if (!Icons.TryGetValue(url, out icon) || icon is { IsCompleted: true, Result: null })
            {
                icon = DownloadAsync(url);
                Icons[url] = icon;
            }
        }

        return icon.WaitAsync(cancellation);
    }

    private static async Task<Bitmap> DownloadAsync(string url)
    {
        try
        {
            byte[] bytes = await FileDownloader.DownloadBytesAsync(url, CancellationToken.None);
            using MemoryStream stream = new(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            // An icon is decoration; a broken one never gets in the way of the list.
            Console.WriteLine($"[PluginStore] Could not load the icon {url}: {ex.Message}");
            return null;
        }
    }
}
