using System.Net;
using System.Net.Http;

namespace LoupixDeck.Services.Updates;

/// <summary>GitHub refused a request because the hourly API limit for this address is used up.</summary>
/// <param name="resetAt">When GitHub accepts requests again; null when the response did not say.</param>
public sealed class GitHubRateLimitException(DateTimeOffset? resetAt, HttpStatusCode statusCode)
    : HttpRequestException(
        resetAt is null
            ? "GitHub API rate limit reached."
            : $"GitHub API rate limit reached until {resetAt.Value.ToLocalTime():HH:mm}.",
        null, statusCode)
{
    public DateTimeOffset? ResetAt { get; } = resetAt;
}
