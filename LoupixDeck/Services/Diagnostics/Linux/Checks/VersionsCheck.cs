using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Updates;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>Reports the application version and the Plugin SDK contract version.</summary>
public sealed class VersionsCheck : ILinuxDiagnosticCheck
{
    public string Id => "system.versions";

    public DiagnosticCategory Category => DiagnosticCategory.System;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string appVersion = AppVersion.Text;
        string sdkVersion = SdkInfo.Version.ToString();

        if (string.IsNullOrWhiteSpace(appVersion))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_VersionsUnreadable")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["app"] = appVersion,
            ["plugin_sdk"] = sdkVersion
        };

        string summary = Loc.Tr("Diagnostics_VersionsFmt", appVersion, sdkVersion);

        if (AppVersion.IsDevelopmentBuild)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_VersionsDevelopmentFmt", appVersion, sdkVersion), null, null, evidence));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title, summary, null, evidence));
    }
}
