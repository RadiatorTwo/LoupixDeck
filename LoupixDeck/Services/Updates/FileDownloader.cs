using System.Net.Http;
using System.Security.Cryptography;

namespace LoupixDeck.Services.Updates;

/// <summary>Streams release files to disk and hashes them, for the app update and the plugin store.</summary>
public static class FileDownloader
{
    /// <summary>No overall timeout: a setup or plugin package can be large and a slow line is not an error.</summary>
    private static readonly HttpClient Http = CreateClient();

    public static async Task DownloadAsync(string url, string path, IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(path);

        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total > 0)
            {
                progress?.Report((double)received / total.Value);
            }
        }
    }

    /// <summary>Downloads a small text file (a manifest or a checksum list) into memory.</summary>
    public static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return await Http.GetStringAsync(url, timeout.Token);
    }

    /// <summary>Upper-case hex SHA-256 of a file.</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LoupixDeck");
        return client;
    }
}
