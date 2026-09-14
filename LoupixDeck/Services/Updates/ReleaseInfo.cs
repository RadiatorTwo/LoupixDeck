namespace LoupixDeck.Services.Updates;

/// <summary>A file attached to a GitHub release.</summary>
/// <param name="Sha256">Hex SHA-256 GitHub computed for the upload (the asset's <c>digest</c>), or null.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, string Sha256);

/// <summary>One stable GitHub release of LoupixDeck or of a plugin.</summary>
public sealed record ReleaseInfo(
    string Tag,
    Version Version,
    string Name,
    string Notes,
    string PageUrl,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset FindAsset(string name)
    {
        return Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A newer version than the installed one: the latest release plus every release between the
/// installed and the latest version, newest first, for the release notes.
/// </summary>
public sealed record UpdateInfo(string InstalledVersion, ReleaseInfo Latest, IReadOnlyList<ReleaseInfo> Releases);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo Update = null, string Error = null);
