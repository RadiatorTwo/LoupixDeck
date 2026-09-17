using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>Reports the distribution and version from os-release.</summary>
public sealed class DistributionCheck : ILinuxDiagnosticCheck
{
    public string Id => "system.distribution";

    public DiagnosticCategory Category => DiagnosticCategory.System;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        IReadOnlyDictionary<string, string> osRelease = LinuxSystemFacts.ReadOsRelease();

        if (osRelease.Count == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DistributionUnreadable")));
        }

        string name = Pick(osRelease, "PRETTY_NAME");

        if (string.IsNullOrWhiteSpace(name))
        {
            string plain = Pick(osRelease, "NAME");
            string version = Pick(osRelease, "VERSION_ID");
            name = string.Join(' ', new[] { plain, version }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DistributionUnreadable")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal);
        AddIfPresent(evidence, osRelease, "ID", "id");
        AddIfPresent(evidence, osRelease, "VERSION_ID", "version_id");
        AddIfPresent(evidence, osRelease, "VARIANT_ID", "variant_id");

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title, name, null, evidence,
            Pick(osRelease, "ID") ?? name));
    }

    private static string Pick(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string value) ? value : null;

    private static void AddIfPresent(Dictionary<string, string> evidence,
        IReadOnlyDictionary<string, string> values, string key, string evidenceKey)
    {
        string value = Pick(values, key);

        if (!string.IsNullOrWhiteSpace(value))
        {
            evidence[evidenceKey] = value;
        }
    }
}
