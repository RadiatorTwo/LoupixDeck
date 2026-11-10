using System.Net.Http;
using System.Runtime.InteropServices;

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

    /// <summary>No self-update is possible and the release page could not be opened either.</summary>
    ReleasePageFailed,

    /// <summary>The release is published, but CI has not uploaded the installer for this platform yet.</summary>
    InstallerNotReady,

    Failed
}

public sealed record UpdateInstallResult(UpdateInstallOutcome Outcome, string Error = null);

public interface IUpdateInstaller
{
    UpdateInstallMode Mode { get; }

    /// <summary>
    /// Whether <paramref name="release"/> already carries the verifiable installer this installation
    /// needs. A release is published before CI has uploaded its files, so a fresh release can be
    /// missing it for a while. Always true when only the release page can be offered.
    /// </summary>
    bool CanInstall(ReleaseInfo release);

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
    public const string WindowsSetupArm64Asset = "LoupixDeck-Setup-win-arm64.exe";
    public const string LinuxScriptAsset = "install-loupixdeck.sh";

    /// <summary>Written next to LoupixDeck.exe by LoupixDeck-Setup (LoupixDeck.Setup/Services/AppPaths.cs).</summary>
    private const string SetupManifestName = "install-manifest.json";

    /// <summary>Install directory of install-loupixdeck.sh.</summary>
    private const string LinuxScriptInstallDir = "/usr/local/lib/loupixdeck";

    /// <summary>Install directory of install-loupixdeck.sh on SteamOS, relative to the user's home.</summary>
    private const string LinuxScriptHomeInstallDir = ".local/lib/loupixdeck";

    /// <summary>The install script a fresh Linux install is made with (see README).</summary>
    private const string MasterScriptUrl =
        $"https://raw.githubusercontent.com/{GitHubReleaseClient.Repository}/master/{LinuxScriptAsset}";

    public UpdateInstallMode Mode { get; } = DetectMode();

    private readonly GitHubReleaseClient _client = new();

    public bool CanInstall(ReleaseInfo release)
    {
        return Mode == UpdateInstallMode.ReleasePage || FindInstaller(release) is not null;
    }

    public async Task<UpdateInstallResult> InstallAsync(ReleaseInfo release, IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (Mode == UpdateInstallMode.ReleasePage)
        {
            return OpenReleasePage(release);
        }

        try
        {
            ReleaseAsset installer = FindInstaller(release);
            if (installer is null)
            {
                // The release may have been read while CI was still uploading its files: ask again
                // instead of trusting the asset list from the update check.
                ReleaseInfo fresh = await RefreshAsync(release, cancellationToken);
                if (fresh is not null)
                {
                    release = fresh;
                    installer = FindInstaller(release);
                }
            }

            if (installer is null && Mode == UpdateInstallMode.LinuxScript)
            {
                // Releases published before the script became a release asset: run the same script a
                // fresh install uses (the README's curl | bash from master). It downloads the release
                // archive itself, so there is no release file of ours to verify here.
                Console.WriteLine($"[Update] {release.Tag} has no {LinuxScriptAsset} asset - using the script from master.");
                installer = new ReleaseAsset(LinuxScriptAsset, MasterScriptUrl, null);
            }

            if (installer is null)
            {
                Console.WriteLine($"[Update] No verifiable installer for {Mode} in {release.Tag} yet.");
                return new UpdateInstallResult(UpdateInstallOutcome.InstallerNotReady);
            }

            string directory = Path.Combine(Path.GetTempPath(), "LoupixDeck-update", release.Version.ToString());
            Directory.CreateDirectory(directory);

            string expected = installer.Sha256;
            string path = Path.Combine(directory, installer.Name);
            await FileDownloader.DownloadAsync(installer.DownloadUrl, path, progress, cancellationToken);

            if (expected is not null)
            {
                string actual = await FileDownloader.ComputeSha256Async(path, cancellationToken);
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
                return OpenReleasePage(release);
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

    /// <summary>The verifiable installer of <paramref name="release"/> for this installation, or null.</summary>
    private ReleaseAsset FindInstaller(ReleaseInfo release)
    {
        return Mode switch
        {
            UpdateInstallMode.WindowsSetup => FindWindowsSetup(release),
            UpdateInstallMode.LinuxScript => release.FindAsset(LinuxScriptAsset) is { Sha256: not null } script
                ? script
                : null,
            _ => null
        };
    }

    /// <summary>The current state of <paramref name="release"/> on GitHub, or null when it cannot be read.</summary>
    private async Task<ReleaseInfo> RefreshAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetReleaseByTagAsync(GitHubReleaseClient.Repository, release.Tag, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Console.WriteLine($"[Update] Could not refresh {release.Tag}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The first verifiable setup for this machine, or null. An ARM64 process never falls back to the
    /// x64 setup: it renders blank windows under emulation, so such a release is not offered here.
    /// </summary>
    private static ReleaseAsset FindWindowsSetup(ReleaseInfo release)
    {
        Architecture process = RuntimeInformation.ProcessArchitecture;
        Architecture os = RuntimeInformation.OSArchitecture;
        foreach (string name in WindowsSetupCandidates(process, os))
        {
            ReleaseAsset asset = release.FindAsset(name);
            if (asset?.Sha256 is not null)
            {
                Console.WriteLine($"[Update] Using {name} (process {process}, OS {os}).");
                return asset;
            }
        }

        Console.WriteLine($"[Update] {release.Tag} has no setup for process {process}, OS {os}.");
        return null;
    }

    /// <summary>
    /// Setup asset names to try, in order. An x64 process on an ARM64 OS prefers the ARM64 setup so an
    /// emulated install migrates to the native build once a release ships one.
    /// </summary>
    internal static string[] WindowsSetupCandidates(Architecture process, Architecture os)
    {
        if (process == Architecture.Arm64)
            return [WindowsSetupArm64Asset];

        if (process == Architecture.X64 && os == Architecture.Arm64)
            return [WindowsSetupArm64Asset, WindowsSetupAsset];

        return [WindowsSetupAsset];
    }

    private static UpdateInstallResult OpenReleasePage(ReleaseInfo release)
    {
        return DetachedProcess.OpenUrl(release.PageUrl)
            ? new UpdateInstallResult(UpdateInstallOutcome.ReleasePageOpened)
            : new UpdateInstallResult(UpdateInstallOutcome.ReleasePageFailed, release.PageUrl);
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
                                      && (processPath.StartsWith(LinuxScriptInstallDir + "/", StringComparison.Ordinal)
                                          || IsInHomeInstallDir(processPath)))
        {
            return UpdateInstallMode.LinuxScript;
        }

        return UpdateInstallMode.ReleasePage;
    }

    /// <summary>True for the SteamOS install, which the script places in the user's home.</summary>
    private static bool IsInHomeInstallDir(string processPath)
    {
        string home = Environment.GetEnvironmentVariable("HOME")
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            return false;

        string installDir = Path.Combine(home, LinuxScriptHomeInstallDir);
        return processPath.StartsWith(installDir + "/", StringComparison.Ordinal);
    }

}
