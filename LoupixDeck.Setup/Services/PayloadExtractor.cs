using System.IO.Compression;
using System.Reflection;

namespace LoupixDeck.Setup.Services;

/// <summary>Outcome of extracting the embedded payload.</summary>
public sealed class ExtractResult
{
    /// <summary>Plugin ids written into the user plugins dir.</summary>
    public List<string> PluginIds { get; } = new();
}

/// <summary>
/// Which parts of the payload to lay out. Selective repair uses <see cref="AppOnly"/> /
/// <see cref="PluginsOnly"/> to touch just the program files or just the plugins.
/// </summary>
public enum ExtractScope
{
    /// <summary>Application files into the install dir AND plugins into the user dir.</summary>
    All,

    /// <summary>Only the application files (install dir); plugin entries are skipped.</summary>
    AppOnly,

    /// <summary>Only the plugins (user dir); application entries are skipped.</summary>
    PluginsOnly
}

/// <summary>
/// Reads the embedded <c>payload.zip</c> (the CI-assembled app + plugins) and lays it out:
/// application files go to the install directory, while <c>plugins/&lt;id&gt;/…</c> entries are
/// relocated to the per-user plugins dir so the user can overwrite/update them (a plugin present in
/// the bundled folder next to the exe would be shadowed and become non-updatable in-app). Any existing
/// plugin <c>settings.json</c> is preserved — it is created by the plugin at runtime and must never be
/// clobbered. After extraction, install-dir files that the current payload did not write are pruned so
/// obsolete files from a previous version don't linger (guarded to only run over an existing, setup-
/// managed install — see <see cref="PruneStaleInstallFiles"/>).
/// </summary>
public sealed class PayloadExtractor
{
    private const string PayloadResourceName = "payload.zip";

    /// <summary>True when a real payload is embedded. False for local dev builds (dry-run UI).</summary>
    public bool HasPayload => Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Any(n => string.Equals(n, PayloadResourceName, StringComparison.OrdinalIgnoreCase));

    private static Stream OpenPayload()
    {
        Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        return s ?? throw new InvalidOperationException("No payload is embedded in this setup build.");
    }

    /// <summary>
    /// Extracts the payload. <paramref name="progress"/> receives a 0..1 fraction and a status line.
    /// </summary>
    public ExtractResult Extract(string installDir, string userPluginsRoot, Action<double, string>? progress = null)
        => Extract(installDir, userPluginsRoot, ExtractScope.All, progress);

    /// <summary>
    /// Extracts the payload, restricted to <paramref name="scope"/>. <paramref name="progress"/> receives
    /// a 0..1 fraction and a status line. Stale-file pruning only runs when application files are being
    /// written (<see cref="ExtractScope.All"/> / <see cref="ExtractScope.AppOnly"/>), never for a
    /// plugins-only pass.
    /// </summary>
    public ExtractResult Extract(string installDir, string userPluginsRoot, ExtractScope scope,
        Action<double, string>? progress = null)
    {
        ExtractResult result = new();
        Directory.CreateDirectory(installDir);

        // Only prune an existing, setup-managed LoupixDeck install — detected by our manifest or the app
        // exe already being present. This guards against wiping unrelated files if the user points the
        // installer at a non-empty foreign directory (a fresh install into an empty dir has nothing to
        // prune anyway). Captured BEFORE extraction so the freshly written files don't mask the check.
        string manifestPath = Path.Combine(installDir, AppPaths.InstallManifestName);
        string appExePath = Path.Combine(installDir, AppPaths.AppExeName);
        bool existingInstall = File.Exists(manifestPath) || File.Exists(appExePath);

        // Full paths of the install-dir files this payload produces; anything else left in the install
        // dir afterwards is obsolete (removed in a newer version) and gets pruned below.
        HashSet<string> writtenInstallFiles = new(StringComparer.OrdinalIgnoreCase);

        using Stream payload = OpenPayload();
        using ZipArchive archive = new(payload, ZipArchiveMode.Read);

        // Only file entries carry data; directory entries have empty names.
        List<ZipArchiveEntry> files = archive.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')).ToList();
        int total = Math.Max(files.Count, 1);
        int done = 0;

        HashSet<string> pluginIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in files)
        {
            string relative = entry.FullName.Replace('\\', '/');
            if (relative.EndsWith('/'))
            {
                done++;
                continue; // pure directory marker
            }

            bool isPlugin = TrySplitPluginEntry(relative, out string pluginId, out string pluginRelative);

            // Honor the requested scope — skip entries the caller isn't repairing.
            if ((isPlugin && scope == ExtractScope.AppOnly) ||
                (!isPlugin && scope == ExtractScope.PluginsOnly))
            {
                done++;
                continue;
            }

            string targetPath = isPlugin
                ? Path.Combine(userPluginsRoot, pluginId, pluginRelative)
                : Path.Combine(installDir, relative.Replace('/', Path.DirectorySeparatorChar));

            if (isPlugin)
                pluginIds.Add(pluginId);

            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            // Preserve a plugin's runtime settings.json — never overwrite an existing one.
            bool isSettings = string.Equals(Path.GetFileName(targetPath), "settings.json", StringComparison.OrdinalIgnoreCase);
            if (isPlugin && isSettings && File.Exists(targetPath))
            {
                done++;
                progress?.Invoke((double)done / total, $"Keeping {pluginId}\\settings.json");
                continue;
            }

            entry.ExtractToFile(targetPath, overwrite: true);
            if (!isPlugin)
                writtenInstallFiles.Add(Path.GetFullPath(targetPath));

            done++;
            progress?.Invoke((double)done / total, isPlugin ? $"Plugin: {pluginId}" : Path.GetFileName(targetPath));
        }

        // Only prune when we actually laid down the app files; a plugins-only pass must not touch the
        // install dir.
        if (existingInstall && scope != ExtractScope.PluginsOnly)
            PruneStaleInstallFiles(installDir, writtenInstallFiles, progress);

        result.PluginIds.AddRange(pluginIds);
        return result;
    }

    /// <summary>
    /// Deletes files under <paramref name="installDir"/> that the current payload did NOT write, so
    /// obsolete files from a previous version don't linger after an update/repair. The dedicated
    /// uninstaller and the install manifest are written by the setup AFTER extraction (not part of the
    /// payload), so they are always kept. Plugin files live outside the install dir and are untouched.
    /// Best-effort per file: a stale leftover that can't be deleted must not fail the whole operation.
    /// </summary>
    private static void PruneStaleInstallFiles(string installDir, HashSet<string> keep,
        Action<double, string>? progress)
    {
        // Non-payload files the setup legitimately places in the install dir — never prune these.
        keep.Add(Path.GetFullPath(Path.Combine(installDir, AppPaths.UninstallerExeName)));
        keep.Add(Path.GetFullPath(Path.Combine(installDir, AppPaths.InstallManifestName)));

        int removed = 0;
        foreach (string file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
        {
            if (keep.Contains(Path.GetFullPath(file)))
                continue;
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                removed++;
            }
            catch
            {
                // best effort — leave leftovers we can't remove rather than aborting the install
            }
        }

        // Drop directories left empty by the pruning (deepest first).
        foreach (string dir in Directory.GetDirectories(installDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, recursive: false);
            }
            catch
            {
                // best effort
            }
        }

        if (removed > 0)
            progress?.Invoke(1.0, $"Removed {removed} obsolete file(s)");
    }

    /// <summary>
    /// Recognises <c>plugins/&lt;id&gt;/rest…</c> entries. Returns the plugin id and the path relative
    /// to the plugin's own folder.
    /// </summary>
    private static bool TrySplitPluginEntry(string relative, out string pluginId, out string pluginRelative)
    {
        pluginId = "";
        pluginRelative = "";

        string prefix = AppPaths.PayloadPluginsFolder + "/";
        if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string rest = relative.Substring(prefix.Length);
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false; // needs plugins/<id>/<file>

        pluginId = rest.Substring(0, slash);
        pluginRelative = rest.Substring(slash + 1).Replace('/', Path.DirectorySeparatorChar);
        return true;
    }
}
