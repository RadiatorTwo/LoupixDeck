using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Plugins;

/// <summary>
/// Builds one check per discovered plugin (issue #258 phase 3). The loader already records why
/// a plugin is not running - disabled, built against another SDK, or a load that threw - so the
/// checks report that state instead of loading anything a second time.
/// </summary>
public sealed class PluginStateCheckSource(IPluginManager plugins) : ILinuxDiagnosticCheckSource
{
    public IReadOnlyList<ILinuxDiagnosticCheck> CreateChecks()
    {
        IReadOnlyList<LoadedPlugin> discovered = plugins.Plugins;

        if (discovered.Count == 0)
        {
            return [new NoPluginCheck()];
        }

        return discovered
            .OrderBy(plugin => plugin.Manifest?.Name ?? plugin.Manifest?.Id, StringComparer.CurrentCulture)
            .Select(ILinuxDiagnosticCheck (plugin) => new PluginStateCheck(plugin))
            .ToList();
    }
}

/// <summary>The state of one discovered plugin, with the loader's own reason when it is not running.</summary>
internal sealed class PluginStateCheck(LoadedPlugin plugin) : ILinuxDiagnosticCheck
{
    public string Id => $"plugin.state:{plugin.Manifest?.Id ?? Path.GetFileName(plugin.Directory)}";

    public DiagnosticCategory Category => DiagnosticCategory.Plugins;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string name = plugin.Manifest?.Name ?? plugin.Manifest?.Id ?? Path.GetFileName(plugin.Directory);
        string title = $"{DiagnosticCheckTitles.For(Id)} — {name}";

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["id"] = plugin.Manifest?.Id ?? "?",
            ["version"] = plugin.Manifest?.Version ?? "?",
            ["sdk_version"] = plugin.Manifest?.SdkVersion ?? "?",
            ["platform"] = plugin.Manifest?.Platform ?? "All",
            ["bundled"] = plugin.IsBundled ? "yes" : "no"
        };

        return Task.FromResult(plugin.Status switch
        {
            PluginLoadStatus.Loaded => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_PluginLoadedFmt", name, plugin.Commands.Count), null, evidence,
                Loc.Tr("Diagnostics_ValueLoaded")),

            PluginLoadStatus.Disabled => DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_PluginDisabledFmt", name), null, Loc.Tr("Diagnostics_ValueDisabled")),

            PluginLoadStatus.Incompatible => DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_PluginIncompatibleFmt", name,
                    plugin.Manifest?.SdkVersion ?? "?"), Shorten(plugin.FailureReason),
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixUpdatePlugin")), evidence,
                Loc.Tr("Diagnostics_ValueIncompatible")),

            _ => DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_PluginFailedFmt", name), Shorten(plugin.FailureReason),
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixReinstallPlugin")), evidence,
                Loc.Tr("Diagnostics_ValueFailed"))
        });
    }

    /// <summary>
    /// The first lines of the loader's reason. A load failure can carry a whole stack trace,
    /// and the detail pane wants the cause, not the trace.
    /// </summary>
    private static string Shorten(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string[] lines = reason.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return string.Join('\n', lines.Take(3)).Trim();
    }
}

/// <summary>The stand-in for an empty Plugins category: nothing was discovered at all.</summary>
internal sealed class NoPluginCheck : ILinuxDiagnosticCheck
{
    public string Id => "plugin.none";

    public DiagnosticCategory Category => DiagnosticCategory.Plugins;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
        => Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, DiagnosticCheckTitles.For(Id),
            Loc.Tr("Diagnostics_NoPluginFound"), null, Loc.Tr("Diagnostics_ValueNone")));
}
