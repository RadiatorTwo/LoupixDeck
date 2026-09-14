using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace LoupixDeck.Services.Updates;

/// <summary>
/// Reads releases from the GitHub API — LoupixDeck's own, or those of a plugin repository. Drafts,
/// pre-releases and tags that are not a plain <c>vX.Y.Z</c> are dropped, so only stable releases
/// are ever offered.
/// </summary>
public sealed class GitHubReleaseClient
{
    public const string Repository = "RadiatorTwo/LoupixDeck";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Stable releases of LoupixDeck, newest version first.</summary>
    /// <exception cref="HttpRequestException">Network failure, rate limit or a non-success status.</exception>
    public Task<IReadOnlyList<ReleaseInfo>> GetStableReleasesAsync(CancellationToken cancellationToken)
    {
        return GetStableReleasesAsync(Repository, cancellationToken);
    }

    /// <summary>Stable releases of <paramref name="repository"/> (<c>owner/name</c>), newest version first.</summary>
    /// <exception cref="HttpRequestException">Network failure, rate limit or a non-success status.</exception>
    public async Task<IReadOnlyList<ReleaseInfo>> GetStableReleasesAsync(string repository,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            $"https://api.github.com/repos/{repository}/releases?per_page=50", cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GitHub API rate limit reached.", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        List<ReleaseInfo> releases = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (GetBool(item, "draft") || GetBool(item, "prerelease"))
            {
                continue;
            }

            string tag = GetString(item, "tag_name");
            Version version = AppVersion.TryParse(tag);
            if (version is null)
            {
                continue;
            }

            List<ReleaseAsset> assets = [];
            if (item.TryGetProperty("assets", out JsonElement assetArray) && assetArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetArray.EnumerateArray())
                {
                    assets.Add(new ReleaseAsset(GetString(asset, "name"), GetString(asset, "browser_download_url"),
                        ParseSha256(GetString(asset, "digest"))));
                }
            }

            releases.Add(new ReleaseInfo(
                tag,
                version,
                GetString(item, "name"),
                GetString(item, "body"),
                GetString(item, "html_url"),
                assets));
        }

        releases.Sort((a, b) => b.Version.CompareTo(a.Version));
        return releases;
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub rejects API requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LoupixDeck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>GitHub reports an asset digest as <c>sha256:&lt;hex&gt;</c>.</summary>
    private static string ParseSha256(string digest)
    {
        const string prefix = "sha256:";
        return digest != null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..]
            : null;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }
}
