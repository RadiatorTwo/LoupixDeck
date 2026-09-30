using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Plugins;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Plugins;

/// <summary>
/// Builds one check per discovered plugin (issue #258 phase 3). The loader already records why
/// a plugin is not running - disabled, built against another SDK, or a load that threw - so the
/// checks report that state instead of loading anything a second time.
/// </summary>
public sealed class PluginStateCheckSource(IPluginManager plugins) : ILinuxDiagnosticCheckSource
{
    /// <summary>Asks every plugin for its requirements again at the start of a run (issue #315), so a
    /// requirement the user fixed - or broke - while the app was open is judged by its current state.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken) => plugins.RefreshRequirementsAsync();

    public IReadOnlyList<ILinuxDiagnosticCheck> CreateChecks()
    {
        IReadOnlyList<LoadedPlugin> discovered = plugins.Plugins;

        if (discovered.Count == 0)
        {
            return [new NoPluginCheck()];
        }

        List<ILinuxDiagnosticCheck> checks = [];

        foreach (LoadedPlugin plugin in discovered
                     .OrderBy(plugin => plugin.Manifest?.Name ?? plugin.Manifest?.Id, StringComparer.CurrentCulture))
        {
            checks.Add(new PluginStateCheck(plugin));

            // One check per requirement the plugin reports (issue #315), met or not: an unmet one warns,
            // and one that was fixed since reads as met, instead of vanishing from the list.
            foreach (PluginRequirement requirement in plugin.Requirements)
                checks.Add(new PluginRequirementCheck(plugin, requirement.Id));
        }

        return checks;
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

/// <summary>
/// One requirement a plugin reports (issue #315). The plugin was asked again just before the run
/// (see PluginStateCheckSource.PrepareAsync), so this reads the current state.
/// </summary>
internal sealed class PluginRequirementCheck(
    LoadedPlugin plugin, string requirementId) : ILinuxDiagnosticCheck
{
    private string PluginId => plugin.Manifest?.Id ?? Path.GetFileName(plugin.Directory);

    public string Id => $"plugin.requirement:{PluginId}:{requirementId}";

    public DiagnosticCategory Category => DiagnosticCategory.Plugins;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
        => Task.FromResult(Evaluate());

    private DiagnosticCheckResult Evaluate()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string pluginName = loc.TrText(plugin.Manifest?.Name ?? PluginId, PluginId);
        PluginRequirement requirement = plugin.Requirements.FirstOrDefault(r => r.Id == requirementId);

        if (requirement == null)
        {
            return DiagnosticCheckResult.Skipped(Id, Category, $"{DiagnosticCheckTitles.For(Id)} — {pluginName}",
                Loc.Tr("Diagnostics_PluginRequirementGoneFmt", pluginName), null, Loc.Tr("Diagnostics_ValueNone"));
        }

        string requirementName = loc.TrText(requirement.Name, PluginId);
        string title = $"{DiagnosticCheckTitles.For(Id)} — {pluginName}: {requirementName}";

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["plugin"] = PluginId,
            ["requirement"] = requirement.Id
        };

        if (requirement.IsMet)
        {
            return DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_PluginRequirementMetFmt", pluginName, requirementName), null, evidence,
                Loc.Tr("Diagnostics_ValueAvailable"));
        }

        string message = loc.TrText(requirement.Message ?? requirement.Name, PluginId);
        string hint = string.IsNullOrWhiteSpace(requirement.InstallHint)
            ? Loc.Tr("Diagnostics_FixPluginRequirement")
            : loc.TrText(requirement.InstallHint, PluginId);

        return DiagnosticCheckResult.Warning(Id, Category, title,
            Loc.Tr("Diagnostics_PluginRequirementFmt", pluginName, message), null,
            new DiagnosticFix(FixKind.Manual, hint), evidence, Loc.Tr("Diagnostics_ValueMissing"));
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
