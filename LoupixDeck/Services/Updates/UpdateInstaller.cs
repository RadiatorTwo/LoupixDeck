using System.Net.Http;
using System.Security.Cryptography;

namespace LoupixDeck.Services.Updates;

/// <summary>How this installation can be updated.</summary>
public enum UpdateInstallMode
{
    /// <summary>Installed by LoupixDeck-Setup: the new setup is downloaded and started.</summary>
    WindowsSetup,

    /// <summary>Installed by install-loupixdeck.sh: the script runs again with the new version.</summary>
    LinuxScript,

    /// <summary>Portable zip, package manager or source build: only the release page is opened.</summary>
    ReleasePage
}

public enum UpdateInstallOutcome
{
    /// <summary>The installer runs; it closes the app on its own.</summary>
    InstallerStarted,

    /// <summary>No self-update is possible, the release page was opened instead.</summary>
    ReleasePageOpened,

    Failed
}

public sealed record UpdateInstallResult(UpdateInstallOutcome Outcome, string Error = null);

public interface IUpdateInstaller
{
    UpdateInstallMode Mode { get; }

    /// <summary>
    /// Downloads the installer of <paramref name="release"/>, verifies it against the SHA-256 digest
    /// GitHub publishes for the asset and starts it detached. Never touches config, macros or the asset store. Never throws.
    /// </summary>
    Task<UpdateInstallResult> InstallAsync(ReleaseInfo release, IProgress<double> progress,
        CancellationToken cancellationToken);
}

public sealed class UpdateInstaller : IUpdateInstaller
{
    public const string WindowsSetupAsset = "LoupixDeck-Setup-win-x64.exe";
    public const string LinuxScriptAsset = "install-loupixdeck.sh";

    /// <summary>Written next to LoupixDeck.exe by LoupixDeck-Setup (LoupixDeck.Setup/Services/AppPaths.cs).</summary>
    private const string SetupManifestName = "install-manifest.json";

    /// <summary>Install directory of install-loupixdeck.sh.</summary>
    private const string LinuxScriptInstallDir = "/usr/local/lib/loupixdeck";

    /// <summary>The install script a fresh Linux install is made with (see README).</summary>
    private const string MasterScriptUrl =
        $"https://raw.githubusercontent.com/{GitHubReleaseClient.Repository}/master/{LinuxScriptAsset}";

    /// <summary>No overall timeout: the setup is large and a slow line is not an error.</summary>
    private static readonly HttpClient Http = CreateClient();

    public UpdateInstallMode Mode { get; } = DetectMode();

    public async Task<UpdateInstallResult> InstallAsync(ReleaseInfo release, IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        string assetName = Mode switch
        {
            UpdateInstallMode.WindowsSetup => WindowsSetupAsset,
            UpdateInstallMode.LinuxScript => LinuxScriptAsset,
            _ => null
        };

        ReleaseAsset installer = assetName is null ? null : release.FindAsset(assetName);
        if (Mode == UpdateInstallMode.LinuxScript && installer?.Sha256 is null)
        {
            // Releases published before the script became a release asset: run the same script a
            // fresh install uses (the README's curl | bash from master). It downloads the release
            // archive itself, so there is no release file of ours to verify here.
            Console.WriteLine($"[Update] {release.Tag} has no {LinuxScriptAsset} asset - using the script from master.");
            installer = new ReleaseAsset(LinuxScriptAsset, MasterScriptUrl, null);
        }

        if (installer is null || (installer.Sha256 is null && Mode != UpdateInstallMode.LinuxScript))
        {
            // Nothing that can be installed and verified for this platform: let the user download it.
            Console.WriteLine($"[Update] No verifiable installer for {Mode} in {release.Tag} - opening the release page.");
            DetachedProcess.OpenUrl(release.PageUrl);
            return new UpdateInstallResult(UpdateInstallOutcome.ReleasePageOpened);
        }

        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "LoupixDeck-update", release.Version.ToString());
            Directory.CreateDirectory(directory);

            string expected = installer.Sha256;
            string path = Path.Combine(directory, installer.Name);
            await DownloadAsync(installer.DownloadUrl, path, progress, cancellationToken);

            if (expected is not null)
            {
                string actual = await ComputeSha256Async(path, cancellationToken);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    return Fail($"Checksum mismatch for {installer.Name} (expected {expected}, got {actual}).");
                }

                Console.WriteLine($"[Update] {installer.Name} verified ({actual}).");
            }

            if (Mode == UpdateInstallMode.WindowsSetup)
            {
                DetachedProcess.StartViaShell(path);
            }
            else if (!DetachedProcess.TryStartInTerminal(["bash", path, release.Tag, "--restart"]))
            {
                Console.WriteLine("[Update] No terminal emulator found - opening the release page.");
                DetachedProcess.OpenUrl(release.PageUrl);
                return new UpdateInstallResult(UpdateInstallOutcome.ReleasePageOpened);
            }

            return new UpdateInstallResult(UpdateInstallOutcome.InstallerStarted);
        }
        catch (OperationCanceledException)
        {
            return Fail("Download cancelled.");
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static UpdateInstallResult Fail(string error)
    {
        Console.WriteLine($"[Update] Update failed: {error}");
        return new UpdateInstallResult(UpdateInstallOutcome.Failed, error);
    }

    private static UpdateInstallMode DetectMode()
    {
        if (OperatingSystem.IsWindows()
            && File.Exists(Path.Combine(AppContext.BaseDirectory, SetupManifestName)))
        {
            return UpdateInstallMode.WindowsSetup;
        }

        // Package-manager installs live elsewhere and only get the notification.
        string processPath = Environment.ProcessPath;
        if (OperatingSystem.IsLinux() && processPath is not null
                                      && processPath.StartsWith(LinuxScriptInstallDir + "/", StringComparison.Ordinal))
        {
            return UpdateInstallMode.LinuxScript;
        }

        return UpdateInstallMode.ReleasePage;
    }

    private static async Task DownloadAsync(string url, string path, IProgress<double> progress,
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
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
