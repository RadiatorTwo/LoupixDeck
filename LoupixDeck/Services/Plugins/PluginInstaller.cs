using System.IO.Compression;
using LoupixDeck.PluginSdk;
using Newtonsoft.Json;

namespace LoupixDeck.Services.Plugins;

/// <summary>Outcome of an install/remove operation, surfaced to the Plugins page.</summary>
public sealed class PluginActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; }

    /// <summary>True when the change only fully takes effect after a restart.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>The plugin id the action targeted, when known (for the reload coordinator).</summary>
    public string PluginId { get; init; }

    public static PluginActionResult Ok(string message, bool requiresRestart = true, string pluginId = null) =>
        new() { Success = true, Message = message, RequiresRestart = requiresRestart, PluginId = pluginId };

    public static PluginActionResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// Local, restart-based plugin lifecycle: install from a zip and remove an
/// installed plugin. Both operate only on the per-user plugins directory
/// (<c>~/.config/LoupixDeck[/debug]/plugins</c>); bundled plugins next to the
/// executable are read-only. Loaded plugins are never hot-unloaded here — a
/// removal that can't delete locked files (Windows holds the entry assembly of a
/// loaded plugin) is deferred to the next startup via a pending-removals marker.
/// </summary>
public interface IPluginInstaller
{
    /// <summary>Validates and installs (or updates) a plugin from a zip archive.</summary>
    Task<PluginActionResult> InstallFromZipAsync(string zipPath);

    /// <summary>Removes an installed (user) plugin and drops it from the enabled list.</summary>
    PluginActionResult Remove(LoadedPlugin plugin);
}

/// <inheritdoc cref="IPluginInstaller"/>
public sealed class PluginInstaller : IPluginInstaller
{
    /// <summary>
    /// Marker file in the user plugins root listing plugin folder names that
    /// couldn't be deleted live (locked assemblies). <see cref="PluginManager"/>
    /// processes it at startup, before any plugin is loaded.
    /// </summary>
    public const string PendingRemovalsFileName = ".pending-removals";

    /// <summary>
    /// Folder in the user plugins root holding staged plugin folders (<c>&lt;id&gt;/…</c>)
    /// for an update whose old version was still loaded (locked) at install time.
    /// <see cref="PluginManager"/> swaps them into place at startup, before loading.
    /// </summary>
    public const string PendingInstallsDirName = ".pending-installs";

    private readonly Models.LoupedeckConfig _config;
    private readonly string _userRoot;
    private readonly string _bundledRoot;

    public PluginInstaller(Models.LoupedeckConfig config)
    {
        _config = config;
        _userRoot = Path.Combine(Utils.FileDialogHelper.GetConfigDir(), "plugins");
        _bundledRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    public async Task<PluginActionResult> InstallFromZipAsync(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return PluginActionResult.Fail("No file selected.");

        var tempDir = Path.Combine(Path.GetTempPath(), "loupixdeck_plugin_" + Guid.NewGuid().ToString("N"));
        try
        {
            return await Task.Run(() => InstallCore(zipPath, tempDir));
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Install failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private PluginActionResult InstallCore(string zipPath, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Could not read the zip archive: {ex.Message}");
        }

        // The manifest sits either at the zip root or inside a single top-level folder.
        var contentRoot = FindContentRoot(tempDir);
        if (contentRoot == null)
            return PluginActionResult.Fail(
                "The zip has no plugin.json (expected at its root or in a single top-level folder).");

        PluginManifest manifest;
        try
        {
            manifest = JsonConvert.DeserializeObject<PluginManifest>(
                File.ReadAllText(Path.Combine(contentRoot, "plugin.json")));
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Invalid plugin.json: {ex.Message}");
        }

        // Same validation the loader applies in PluginManager.LoadOne.
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) ||
            string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            return PluginActionResult.Fail("plugin.json is missing 'id' or 'entryAssembly'.");

        if (!IsSafeFolderName(manifest.Id))
            return PluginActionResult.Fail($"Plugin id '{manifest.Id}' is not a valid folder name.");

        if (!Version.TryParse(manifest.SdkVersion, out var pluginSdk))
            return PluginActionResult.Fail($"Unparseable sdkVersion '{manifest.SdkVersion}'.");

        if (pluginSdk.Major != SdkInfo.Version.Major)
            return PluginActionResult.Fail(
                $"Plugin SDK {pluginSdk} is incompatible with this app's SDK {SdkInfo.Version}.");

        if (!File.Exists(Path.Combine(contentRoot, manifest.EntryAssembly)))
            return PluginActionResult.Fail(
                $"Entry assembly '{manifest.EntryAssembly}' is missing from the zip.");

        // A bundled plugin with the same id always shadows a user copy (PluginManager
        // scans the app dir first), so installing one would silently do nothing.
        if (Directory.Exists(Path.Combine(_bundledRoot, manifest.Id)))
            return PluginActionResult.Fail($"'{manifest.Id}' is a built-in plugin and cannot be replaced.");

        Directory.CreateDirectory(_userRoot);
        var targetDir = Path.Combine(_userRoot, manifest.Id);
        var name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name;

        // Enable it right away so the reload coordinator loads it (and a restart
        // would too) — installing a chosen package implies the user wants it active
        // (symmetric with Remove, which drops the id). Persisted on dialog close.
        EnsureEnabled(manifest.Id);

        // Fresh install — id is new, nothing is locked. Coordinator loads it live.
        if (!Directory.Exists(targetDir))
        {
            try
            {
                CopyDirectory(contentRoot, targetDir);
            }
            catch (Exception ex)
            {
                return PluginActionResult.Fail($"Could not copy the plugin into place: {ex.Message}");
            }

            return PluginActionResult.Ok(
                $"Installed '{name}' v{manifest.Version}.", requiresRestart: false, pluginId: manifest.Id);
        }

        // Update/replace — capture the old version for the message.
        var previousVersion = TryReadInstalledVersion(targetDir);
        var arrow = string.Equals(previousVersion, manifest.Version, StringComparison.OrdinalIgnoreCase)
            ? $"v{manifest.Version}, reinstalled"
            : $"{previousVersion} → {manifest.Version}";

        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old version is loaded (Windows locks its assemblies). Stage the new
            // files; PluginManager swaps them into place at the next startup, before
            // anything is loaded. The coordinator unloads the old version live so its
            // commands stop now — only the on-disk swap waits for the restart.
            if (StageForInstall(manifest.Id, contentRoot))
                return PluginActionResult.Ok(
                    $"Updated '{name}' ({arrow}). Restart to load the new version.",
                    requiresRestart: true, pluginId: manifest.Id);

            return PluginActionResult.Fail($"Could not stage the update for '{name}': {ex.Message}");
        }

        try
        {
            CopyDirectory(contentRoot, targetDir);
        }
        catch (Exception ex)
        {
            return PluginActionResult.Fail($"Could not copy the plugin into place: {ex.Message}");
        }

        return PluginActionResult.Ok(
            $"Updated '{name}' ({arrow}).", requiresRestart: false, pluginId: manifest.Id);
    }

    public PluginActionResult Remove(LoadedPlugin plugin)
    {
        if (plugin?.Manifest == null || string.IsNullOrWhiteSpace(plugin.Directory))
            return PluginActionResult.Fail("This plugin cannot be removed.");

        // Only user-installed plugins are deletable; bundled ones are read-only.
        if (!IsUnderUserRoot(plugin.Directory))
            return PluginActionResult.Fail("Built-in plugins cannot be removed.");

        var id = plugin.Manifest.Id;
        var name = string.IsNullOrWhiteSpace(plugin.Manifest.Name) ? id : plugin.Manifest.Name;

        // Drop it from the enabled list regardless of how the delete goes; this is
        // persisted when the Settings dialog closes (same path as the toggle).
        if (!string.IsNullOrWhiteSpace(id))
            _config.EnabledPlugins?.RemoveAll(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase));

        try
        {
            if (Directory.Exists(plugin.Directory))
                Directory.Delete(plugin.Directory, recursive: true);

            return PluginActionResult.Ok($"Removed '{name}'. Restart to fully unload it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A loaded plugin's assemblies are locked on Windows — defer the delete
            // to the next startup, before the plugin is loaded again.
            if (MarkForRemoval(plugin.Directory))
                return PluginActionResult.Ok(
                    $"'{name}' is in use; it will be deleted on the next restart.");

            return PluginActionResult.Fail($"Could not remove '{name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes any folders listed in the pending-removals marker, then removes the
    /// marker. Called at startup before plugins are loaded, when nothing is locked.
    /// </summary>
    public static void ProcessPendingRemovals(string userRoot)
    {
        var markerPath = Path.Combine(userRoot, PendingRemovalsFileName);
        if (!File.Exists(markerPath))
            return;

        try
        {
            foreach (var rawLine in File.ReadAllLines(markerPath))
            {
                var folderName = rawLine.Trim();
                // Only ever delete a direct child folder of the user root.
                if (folderName.Length == 0 || !IsSafeFolderName(folderName))
                    continue;

                var dir = Path.Combine(userRoot, folderName);
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch (Exception ex) { Console.WriteLine($"PluginInstaller: pending removal of '{dir}' failed: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginInstaller: could not process pending removals: {ex.Message}");
        }
        finally
        {
            try { File.Delete(markerPath); } catch { /* best effort */ }
        }
    }

    private bool StageForInstall(string id, string contentRoot)
    {
        try
        {
            var stagingRoot = Path.Combine(_userRoot, PendingInstallsDirName);
            Directory.CreateDirectory(stagingRoot);

            var staged = Path.Combine(stagingRoot, id);
            if (Directory.Exists(staged))
                Directory.Delete(staged, recursive: true);

            CopyDirectory(contentRoot, staged);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginInstaller: could not stage update for '{id}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Swaps any staged plugin folders into place (delete the old <c>&lt;id&gt;</c>,
    /// move the staged one in), then removes the staging area. Called at startup
    /// before plugins are loaded, when nothing is locked.
    /// </summary>
    public static void ProcessPendingInstalls(string userRoot)
    {
        var stagingRoot = Path.Combine(userRoot, PendingInstallsDirName);
        if (!Directory.Exists(stagingRoot))
            return;

        try
        {
            foreach (var staged in Directory.GetDirectories(stagingRoot))
            {
                var id = Path.GetFileName(staged);
                if (!IsSafeFolderName(id))
                    continue;

                var target = Path.Combine(userRoot, id);
                try
                {
                    if (Directory.Exists(target))
                        Directory.Delete(target, recursive: true);
                    Directory.Move(staged, target);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PluginInstaller: pending install of '{id}' failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginInstaller: could not process pending installs: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(stagingRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private void EnsureEnabled(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        _config.EnabledPlugins ??= [];
        if (!_config.EnabledPlugins.Any(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase)))
            _config.EnabledPlugins.Add(id);
    }

    private bool MarkForRemoval(string pluginDir)
    {
        try
        {
            var folderName = Path.GetFileName(pluginDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!IsSafeFolderName(folderName))
                return false;

            var markerPath = Path.Combine(_userRoot, PendingRemovalsFileName);
            var existing = File.Exists(markerPath)
                ? File.ReadAllLines(markerPath).Select(l => l.Trim()).Where(l => l.Length > 0)
                : Enumerable.Empty<string>();

            var lines = existing.Append(folderName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            File.WriteAllLines(markerPath, lines);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginInstaller: could not mark '{pluginDir}' for removal: {ex.Message}");
            return false;
        }
    }

    private bool IsUnderUserRoot(string dir)
    {
        try
        {
            var root = Path.GetFullPath(_userRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(dir);
            var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(root, cmp);
        }
        catch
        {
            return false;
        }
    }

    private static string FindContentRoot(string tempDir)
    {
        if (File.Exists(Path.Combine(tempDir, "plugin.json")))
            return tempDir;

        // Tolerate a single wrapper folder (e.g. the zip contains "MyPlugin/...").
        var subDirs = Directory.GetDirectories(tempDir);
        if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "plugin.json")))
            return subDirs[0];

        return null;
    }

    private static string TryReadInstalledVersion(string dir)
    {
        try
        {
            var manifest = JsonConvert.DeserializeObject<PluginManifest>(
                File.ReadAllText(Path.Combine(dir, "plugin.json")));
            return string.IsNullOrWhiteSpace(manifest?.Version) ? "?" : manifest.Version;
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>True when the name is a single path segment with no traversal characters.</summary>
    private static bool IsSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return name.IndexOf('/') < 0 && name.IndexOf('\\') < 0;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
