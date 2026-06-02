using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services
{
    /// <summary>
    /// Downloads, installs and uninstalls the Interception kernel driver on Windows.
    ///
    /// The driver is intentionally NOT bundled with LoupixDeck: this service fetches the latest
    /// official release from GitHub, extracts the x64 interception.dll next to the executable
    /// (so <see cref="InterceptionKeyboard"/> can P/Invoke it) and runs the official command-line
    /// installer elevated. Installing/uninstalling the kernel driver requires a reboot.
    /// </summary>
    public interface IInterceptionService
    {
        /// <summary>True when the driver is loaded (probed via the interception.dll API).</summary>
        bool IsDriverInstalled();

        /// <summary>Downloads the latest release and runs the elevated installer. Reboot required.</summary>
        Task<bool> DownloadAndInstallAsync(IProgress<string> progress);

        /// <summary>Runs the elevated uninstaller. Reboot required.</summary>
        Task<bool> UninstallAsync(IProgress<string> progress);
    }

    public class InterceptionService(InterceptionKeyboard interceptionKeyboard) : IInterceptionService
    {
        private const string ReleaseApiUrl =
            "https://api.github.com/repos/oblitum/Interception/releases/latest";

        private const string InstallerName = "install-interception.exe";
        private const string LibraryName = "interception.dll";

        public bool IsDriverInstalled() => interceptionKeyboard.IsDriverAvailable();

        public async Task<bool> DownloadAndInstallAsync(IProgress<string> progress)
        {
            try
            {
                var (installerPath, dllPath) = await DownloadAndExtractAsync(progress);
                if (installerPath == null)
                    return false;

                // Place the library + a local copy of the installer next to the executable so
                // the keyboard backend can load the DLL and a later uninstall can reuse the tool.
                var appDir = AppContext.BaseDirectory;
                if (dllPath != null)
                {
                    progress.Report("Copying interception.dll next to the application...");
                    File.Copy(dllPath, Path.Combine(appDir, LibraryName), overwrite: true);
                }

                var localInstaller = Path.Combine(appDir, InstallerName);
                File.Copy(installerPath, localInstaller, overwrite: true);

                progress.Report("Starting installation (administrator confirmation required)...");
                var ok = await RunElevatedAsync(localInstaller, "/install");
                if (!ok)
                {
                    progress.Report("Installation cancelled or failed.");
                    return false;
                }

                progress.Report("Installation complete. Please restart your computer for the driver to become active.");
                return true;
            }
            catch (Exception ex)
            {
                progress.Report($"Installation error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UninstallAsync(IProgress<string> progress)
        {
            try
            {
                // Prefer the installer copy placed during install; otherwise re-fetch the release.
                var installerPath = Path.Combine(AppContext.BaseDirectory, InstallerName);
                if (!File.Exists(installerPath))
                {
                    progress.Report("Installer required — downloading release...");
                    (installerPath, _) = await DownloadAndExtractAsync(progress);
                    if (installerPath == null)
                        return false;
                }

                progress.Report("Starting uninstallation (administrator confirmation required)...");
                var ok = await RunElevatedAsync(installerPath, "/uninstall");
                if (!ok)
                {
                    progress.Report("Uninstallation cancelled or failed.");
                    return false;
                }

                progress.Report("Uninstallation complete. Please restart your computer.");
                return true;
            }
            catch (Exception ex)
            {
                progress.Report($"Uninstallation error: {ex.Message}");
                return false;
            }
        }

        // Resolves the latest release, downloads the zip and extracts it to a temp folder.
        // Returns the paths to install-interception.exe and the x64 interception.dll inside it.
        private static async Task<(string installerPath, string dllPath)> DownloadAndExtractAsync(
            IProgress<string> progress)
        {
            using var http = new HttpClient();
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LoupixDeck");

            progress.Report("Querying latest Interception release...");
            var json = await http.GetStringAsync(ReleaseApiUrl);
            var release = JObject.Parse(json);

            var assets = release["assets"] as JArray;
            var downloadUrl = assets?
                .FirstOrDefault(a => (a["name"]?.ToString() ?? "")
                    .EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?["browser_download_url"]
                ?.ToString();

            if (string.IsNullOrEmpty(downloadUrl))
            {
                progress.Report("No matching release asset (.zip) found.");
                return (null, null);
            }

            progress.Report("Downloading Interception...");
            var tempZip = Path.Combine(Path.GetTempPath(), $"interception_{Guid.NewGuid():N}.zip");
            var bytes = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempZip, bytes);

            progress.Report("Extracting...");
            var tempDir = Path.Combine(Path.GetTempPath(), $"interception_{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(tempZip, tempDir);

            var installerPath = Directory
                .EnumerateFiles(tempDir, InstallerName, SearchOption.AllDirectories)
                .FirstOrDefault();
            var dllPath = Directory
                .EnumerateFiles(tempDir, LibraryName, SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Replace('\\', '/').Contains("/x64/", StringComparison.OrdinalIgnoreCase));

            if (installerPath == null)
                progress.Report("Installer im Archiv nicht gefunden.");

            return (installerPath, dllPath);
        }

        // Runs the given executable elevated (UAC). Returns false if the user declines the
        // prompt (Win32Exception) or the process exits with a non-zero code.
        private static async Task<bool> RunElevatedAsync(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true, // required for the "runas" verb
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                // User cancelled the UAC prompt.
                return false;
            }
        }
    }

    /// <summary>
    /// Stand-in registered on platforms without Interception (Linux) so the settings view model
    /// can always resolve <see cref="IInterceptionService"/>. The Interception settings page is
    /// hidden there, so these methods are never reached in practice.
    /// </summary>
    public class NoOpInterceptionService : IInterceptionService
    {
        public bool IsDriverInstalled() => false;

        public Task<bool> DownloadAndInstallAsync(IProgress<string> progress)
        {
            progress.Report("Interception is not available on this platform.");
            return Task.FromResult(false);
        }

        public Task<bool> UninstallAsync(IProgress<string> progress)
        {
            progress.Report("Interception is not available on this platform.");
            return Task.FromResult(false);
        }
    }
}
