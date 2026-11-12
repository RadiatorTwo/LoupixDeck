using System.Net.Http;
using LoupixDeck.Localization;
using LoupixDeck.Services.Updates;
using LoupixDeck.Utils;
using Newtonsoft.Json;

namespace LoupixDeck.Services.ProfileStore;

/// <summary>The community profile list as loaded for the store window.</summary>
/// <param name="Entries">The entries; empty when nothing could be loaded.</param>
/// <param name="Error">Why the list could not be downloaded; null when it was.</param>
/// <param name="IsFromCache">True when the entries are the copy saved on disk by an earlier download.</param>
/// <param name="CacheTime">When that copy was saved; null when unknown or not from the cache.</param>
public sealed record ProfileStoreResult(
    IReadOnlyList<ProfileCatalogEntry> Entries,
    string Error = null,
    bool IsFromCache = false,
    DateTimeOffset? CacheTime = null);

/// <summary>A downloaded and verified package, or why there is none.</summary>
public sealed record ProfileDownloadResult(string PackagePath, string Error = null)
{
    public bool Success => Error is null;
}

/// <summary>
/// Reads the community profile list (issue #587) and downloads its packages. Read-only: sharing goes
/// through a pull request to the profiles repository, never through the app.
/// </summary>
public interface IProfileStoreService
{
    /// <summary>
    /// The list. Reuses a recent download unless <paramref name="force"/>; falls back to the copy on disk
    /// when the download fails.
    /// </summary>
    Task<ProfileStoreResult> GetEntriesAsync(bool force, CancellationToken cancellationToken = default);

    /// <summary>Downloads the entry's package into a temp folder and checks it against the listed SHA-256.</summary>
    Task<ProfileDownloadResult> DownloadAsync(ProfileCatalogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Removes a package returned by <see cref="DownloadAsync"/>, with its temp folder.</summary>
    void DeleteDownload(string packagePath);
}

public sealed class ProfileStoreService : IProfileStoreService
{
    public const string Repository = "RadiatorTwo/LoupixDeck.Profiles";

    public const string DefaultCatalogUrl = $"https://raw.githubusercontent.com/{Repository}/master/index.json";

    /// <summary><c>ui-settings.json</c> key overriding the list location (a URL or a local file) for testing.</summary>
    private const string CatalogUrlKey = "ProfileStoreCatalogUrl";

    private const string CatalogCacheFileName = "profile-store-cache.json";

    private const string DownloadFolderName = "LoupixDeck-profiles";

    /// <summary>How long a downloaded list is reused when the window is reopened; Refresh ignores it.</summary>
    private static readonly TimeSpan CatalogFreshness = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ProfileCatalog _catalog;
    private DateTimeOffset? _catalogLoadedAt;

    public async Task<ProfileStoreResult> GetEntriesAsync(bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _catalog is not null && _catalogLoadedAt is { } loadedAt
                && DateTimeOffset.UtcNow - loadedAt < CatalogFreshness)
            {
                return new ProfileStoreResult(Usable(_catalog));
            }

            try
            {
                ProfileCatalog catalog = await LoadCatalogAsync(cancellationToken);
                return new ProfileStoreResult(Usable(catalog));
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                Console.WriteLine($"[ProfileStore] Could not load the profile list: {ex.Message}");
                string error = Loc.Tr("ProfileStore_CatalogUnavailable", ex.Message);
                (ProfileCatalog cached, DateTimeOffset? writtenAt) = ReadCatalogCache();
                return cached is null
                    ? new ProfileStoreResult([], error)
                    : new ProfileStoreResult(Usable(cached), error, true, writtenAt);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileDownloadResult> DownloadAsync(ProfileCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ProfileCatalogPackage package = entry.Package;
        string directory = Path.Combine(Path.GetTempPath(), DownloadFolderName, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);

            // The file name comes from the list; only its last segment is used, so it cannot leave the folder.
            string fileName = Path.GetFileName(package.FileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = entry.Id + ".loupixprofile";
            }

            string path = Path.Combine(directory, fileName);
            await FileDownloader.DownloadAsync(package.DownloadUrl, path, null, cancellationToken);

            // Verify before the package is ever opened.
            string actual = await FileDownloader.ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(directory);
                Console.WriteLine(
                    $"[ProfileStore] Checksum mismatch for {fileName} (expected {package.Sha256}, got {actual}).");
                return new ProfileDownloadResult(null, Loc.Tr("ProfileStore_ChecksumMismatch", entry.DisplayName));
            }

            Console.WriteLine($"[ProfileStore] {fileName} verified ({actual}).");
            return new ProfileDownloadResult(path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteDirectory(directory);
            return new ProfileDownloadResult(null, Loc.Tr("ProfileStore_DownloadCancelled"));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            TryDeleteDirectory(directory);
            Console.WriteLine($"[ProfileStore] Download of {entry.Id} failed: {ex.Message}");
            return new ProfileDownloadResult(null, Loc.Tr("ProfileStore_DownloadFailed", ex.Message));
        }
    }

    public void DeleteDownload(string packagePath)
    {
        if (string.IsNullOrEmpty(packagePath))
        {
            return;
        }

        string directory = Path.GetDirectoryName(packagePath);
        if (directory is not null && Directory.GetParent(directory)?.Name == DownloadFolderName)
        {
            TryDeleteDirectory(directory);
        }
    }

    /// <summary>Entries the store can show and install: an id and a package with a URL and a checksum.</summary>
    private static List<ProfileCatalogEntry> Usable(ProfileCatalog catalog)
    {
        return catalog.Profiles
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id)
                                      && !string.IsNullOrWhiteSpace(e.Package?.DownloadUrl)
                                      && !string.IsNullOrWhiteSpace(e.Package?.Sha256))
            .ToList();
    }

    /// <summary>True when the location is an http(s) URL; anything else is a path on this machine.</summary>
    private static bool IsWebLocation(string location)
    {
        return Uri.TryCreate(location, UriKind.Absolute, out Uri uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private async Task<ProfileCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        string location = UiSettingsStore.GetString(CatalogUrlKey);
        if (string.IsNullOrWhiteSpace(location))
        {
            location = DefaultCatalogUrl;
        }

        string json = IsWebLocation(location)
            ? await FileDownloader.DownloadStringAsync(location, cancellationToken)
            : await File.ReadAllTextAsync(location, cancellationToken);

        ProfileCatalog catalog = Parse(json) ?? throw new InvalidOperationException("The profile list is empty.");
        if (catalog.SchemaVersion > ProfileCatalog.SupportedSchemaVersion)
        {
            Console.WriteLine($"[ProfileStore] The profile list uses schema version {catalog.SchemaVersion}; " +
                              $"reading it as version {ProfileCatalog.SupportedSchemaVersion} and ignoring what is unknown.");
        }

        _catalog = catalog;
        _catalogLoadedAt = DateTimeOffset.UtcNow;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(FileDialogHelper.GetConfigDir(), CatalogCacheFileName), json,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ProfileStore] Could not cache the profile list: {ex.Message}");
        }

        return catalog;
    }

    private static (ProfileCatalog Catalog, DateTimeOffset? WrittenAt) ReadCatalogCache()
    {
        string path = Path.Combine(FileDialogHelper.GetConfigDir(), CatalogCacheFileName);
        try
        {
            if (!File.Exists(path))
            {
                return (null, null);
            }

            ProfileCatalog catalog = Parse(File.ReadAllText(path));
            return (catalog, catalog is null ? null : new DateTimeOffset(File.GetLastWriteTime(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[ProfileStore] Could not read the cached profile list: {ex.Message}");
            return (null, null);
        }
    }

    private static ProfileCatalog Parse(string json)
    {
        ProfileCatalog catalog = JsonConvert.DeserializeObject<ProfileCatalog>(json);
        if (catalog is not null)
        {
            catalog.Profiles ??= [];
        }

        return catalog;
    }

    private static bool IsExpectedFailure(Exception ex)
    {
        // A location the user typed is data: a malformed one must read as "the list is unavailable".
        return ex is HttpRequestException or TaskCanceledException or JsonException or IOException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException
            or FormatException or ArgumentException;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best-effort.
        }
    }
}
