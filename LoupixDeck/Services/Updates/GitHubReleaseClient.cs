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
    /// <remarks>
    /// Sends the ETag of the last response for the same URL; an unchanged list comes back as
    /// an empty <c>304 Not Modified</c> and is read from the cache. The request itself still counts against the
    /// hourly limit, because it is unauthenticated.
    /// </remarks>
    /// <exception cref="GitHubRateLimitException">The hourly API limit is used up.</exception>
    /// <exception cref="HttpRequestException">Network failure or another non-success status.</exception>
    public async Task<IReadOnlyList<ReleaseInfo>> GetStableReleasesAsync(string repository,
        CancellationToken cancellationToken)
    {
        string url = ReleasesUrl(repository);
        GitHubResponseCache.Entry cached = GitHubResponseCache.Get(url);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (cached is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);

        string json;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            json = cached.Body;
        }
        else
        {
            ThrowIfRateLimited(response);
            response.EnsureSuccessStatusCode();

            json = await response.Content.ReadAsStringAsync(cancellationToken);
            GitHubResponseCache.Set(url, response.Headers.ETag?.ToString(), json);
        }

        return ParseReleases(json);
    }

    /// <summary>
    /// The stable releases of <paramref name="repository"/> as last read from GitHub, from the on-disk cache;
    /// null when there is none. Never touches the network.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> GetCachedStableReleases(string repository)
    {
        string body = GitHubResponseCache.Get(ReleasesUrl(repository))?.Body;
        if (body is null)
        {
            return null;
        }

        try
        {
            return ParseReleases(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReleasesUrl(string repository)
    {
        return $"https://api.github.com/repos/{repository}/releases?per_page=50";
    }

    private static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

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

    /// <summary>
    /// GitHub answers a used-up limit with 429, or with 403 and <c>X-RateLimit-Remaining: 0</c>. The reset time
    /// comes from <c>X-RateLimit-Reset</c> (Unix seconds) or <c>Retry-After</c> (seconds).
    /// </summary>
    private static void ThrowIfRateLimited(HttpResponseMessage response)
    {
        bool limited = response.StatusCode == HttpStatusCode.TooManyRequests
                       || (response.StatusCode == HttpStatusCode.Forbidden
                           && GetHeader(response, "X-RateLimit-Remaining") == "0");
        if (!limited)
        {
            return;
        }

        DateTimeOffset? resetAt = null;
        if (long.TryParse(GetHeader(response, "X-RateLimit-Reset"), out long unixSeconds))
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        else if (response.Headers.RetryAfter?.Delta is { } delay)
        {
            resetAt = DateTimeOffset.UtcNow + delay;
        }

        throw new GitHubRateLimitException(resetAt, response.StatusCode);
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string> values) ? values.FirstOrDefault() : null;
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
