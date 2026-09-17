using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Plugins;
using Newtonsoft.Json;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Plugins;

/// <summary>
/// Looks at the plugin folders the loader never reported on. A folder whose manifest is missing,
/// unparseable or incomplete is not a failed plugin - it is not a plugin at all, so it never
/// appears in the plugin list and the user is left with a folder that does nothing.
///
/// A manifest that declares another platform is reported here too: it is a correct manifest for
/// a machine this is not, which is a different statement from "the load failed".
/// </summary>
public sealed class PluginManifestCheck : ILinuxDiagnosticCheck
{
    public string Id => "plugins.manifests";

    public DiagnosticCategory Category => DiagnosticCategory.Plugins;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        (string bundled, string user) = PluginManager.GetPluginRoots();

        List<string> broken = [];
        List<string> foreign = [];

        // A plugin id present in both roots is ONE plugin to the loader: ResolvePlugins picks a
        // winner, the user copy overriding the bundled one. Counting both would report two valid
        // manifests for what the plugin list shows as a single entry, so ids are collected and
        // counted once. The user root is walked first, so it wins the id.
        HashSet<string> valid = new(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in Folders(user).Concat(Folders(bundled)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string manifestPath = Path.Combine(folder, "manifest.json");
            string name = Path.GetFileName(folder);

            if (!File.Exists(manifestPath))
            {
                broken.Add($"{name}: no manifest.json");
                continue;
            }

            PluginManifest manifest;

            try
            {
                manifest = JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                broken.Add($"{name}: {ex.Message}");
                continue;
            }

            if ((manifest == null) || string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            {
                broken.Add($"{name}: manifest without an id or an entry assembly");
                continue;
            }

            if (!File.Exists(Path.Combine(folder, manifest.EntryAssembly)))
            {
                broken.Add($"{name}: {manifest.EntryAssembly} is missing");
                continue;
            }

            if (!SupportsThisPlatform(manifest.Platform))
            {
                foreign.Add($"{name}: {manifest.Platform}");
                continue;
            }

            valid.Add(manifest.Id);
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["valid"] = valid.Count.ToString(),
            ["broken"] = broken.Count.ToString(),
            ["other_platform"] = foreign.Count.ToString()
        };

        if (broken.Count > 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_PluginManifestsBrokenFmt", broken.Count),
                string.Join("\n", broken),
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixReinstallPlugin")), evidence,
                Loc.Tr("Diagnostics_ValueBrokenFmt", broken.Count)));
        }

        if (foreign.Count > 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_PluginManifestsForeignFmt", foreign.Count),
                string.Join("\n", foreign), null, evidence,
                Loc.Tr("Diagnostics_ValueOtherPlatform")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_PluginManifestsOkFmt", valid.Count), null, evidence,
            Loc.Tr("Diagnostics_ValueValidFmt", valid.Count)));
    }

    /// <summary>The manifest's Platform value against the running OS ("All" matches everything).</summary>
    private static bool SupportsThisPlatform(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform) ||
            platform.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return platform.Equals(OperatingSystem.IsWindows() ? "Windows" : "Linux",
            StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Folders(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
