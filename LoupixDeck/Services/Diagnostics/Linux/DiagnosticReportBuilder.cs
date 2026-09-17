using System.Globalization;
using System.Text;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Updates;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Turns a run into the Markdown a user pastes into a bug report.
///
/// Every value passes through <see cref="DiagnosticReportSanitizer"/>, and the finished text
/// passes through it once more. The report is written in the current UI language, because the
/// preview has to show exactly the text that gets copied.
/// </summary>
public static class DiagnosticReportBuilder
{
    /// <summary>Builds the report text for <paramref name="run"/>.</summary>
    public static string Build(DiagnosticRunResult run)
    {
        StringBuilder report = new();

        AppendHeader(report, run);

        foreach (DiagnosticCategory category in run.Results.Select(result => result.Category).Distinct())
        {
            AppendCategoryTable(report, category,
                run.Results.Where(result => result.Category == category).ToList());
        }

        AppendDetails(report, run.Results.Where(result => result.Status != DiagnosticStatus.Pass).ToList());

        report.Append("> ").Append(Loc.Tr("Diagnostics_PlaybackIsNotRecording")).Append('\n');
        report.Append('\n');
        report.Append('*').Append(Loc.Tr("Diagnostics_ReportFooter")).Append("*\n");

        // Backstop: a check that starts emitting something identifying is caught here even if
        // the value was not scrubbed on the way in.
        return DiagnosticReportSanitizer.Scrub(report.ToString());
    }

    private static void AppendHeader(StringBuilder report, DiagnosticRunResult run)
    {
        report.Append("# ").Append(Loc.Tr("Diagnostics_ReportHeading")).Append("\n\n");

        string generated = run.CompletedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        report.Append("- ").Append(Loc.Tr("Diagnostics_ReportGeneratedFmt", generated)).Append('\n');
        report.Append("- ").Append(Loc.Tr("Diagnostics_ReportAppVersionFmt", AppVersion.Text ?? "?")).Append('\n');
        report.Append("- ").Append(Loc.Tr("Diagnostics_ReportSdkVersionFmt", SdkInfo.Version)).Append('\n');
        report.Append("- ").Append(CountsLine(run.Results)).Append('\n');

        if (run.WasCancelled)
        {
            report.Append("- ").Append(Loc.Tr("Diagnostics_ReportCancelled")).Append('\n');
        }

        report.Append('\n');
    }

    private static string CountsLine(IReadOnlyList<DiagnosticCheckResult> results)
    {
        return Loc.Tr("Diagnostics_CountsFmt",
            results.Count(result => result.Status == DiagnosticStatus.Pass),
            results.Count(result => result.Status == DiagnosticStatus.Warning),
            results.Count(result => result.Status == DiagnosticStatus.Fail),
            results.Count(result => result.Status == DiagnosticStatus.Unknown),
            results.Count(result => result.Status == DiagnosticStatus.Skipped));
    }

    private static void AppendCategoryTable(StringBuilder report, DiagnosticCategory category,
        IReadOnlyList<DiagnosticCheckResult> results)
    {
        report.Append("## ").Append(DiagnosticText.CategoryTitle(category)).Append("\n\n");
        report.Append("| ").Append(Cell(Loc.Tr("Diagnostics_ReportColumnCheck")))
            .Append(" | ").Append(Cell(Loc.Tr("Diagnostics_ReportColumnStatus")))
            .Append(" | ").Append(Cell(Loc.Tr("Diagnostics_ReportColumnSummary")))
            .Append(" |\n");
        report.Append("| --- | --- | --- |\n");

        foreach (DiagnosticCheckResult result in results)
        {
            report.Append("| ").Append(Cell(result.Title))
                .Append(" | ").Append(Cell(DiagnosticText.StatusText(result.Status)))
                .Append(" | ").Append(Cell(result.Summary))
                .Append(" |\n");
        }

        report.Append('\n');
    }

    private static void AppendDetails(StringBuilder report, IReadOnlyList<DiagnosticCheckResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        report.Append("## ").Append(Loc.Tr("Diagnostics_ReportDetailsHeading")).Append("\n\n");

        foreach (DiagnosticCheckResult result in results)
        {
            report.Append("### ").Append(Scrub(result.Title)).Append(" — ")
                .Append(DiagnosticText.StatusText(result.Status)).Append("\n\n");
            report.Append(Scrub(result.Summary)).Append("\n\n");

            AppendEvidence(report, result);
            AppendTechnicalDetail(report, result);
            AppendFix(report, result);
        }
    }

    private static void AppendEvidence(StringBuilder report, DiagnosticCheckResult result)
    {
        if (result.Evidence.Count == 0)
        {
            return;
        }

        report.Append(Loc.Tr("Diagnostics_Evidence")).Append(":\n\n");

        foreach (KeyValuePair<string, string> entry in result.Evidence)
        {
            report.Append("- `").Append(Scrub(entry.Key)).Append("`: ")
                .Append(Scrub(entry.Value)).Append('\n');
        }

        report.Append('\n');
    }

    private static void AppendTechnicalDetail(StringBuilder report, DiagnosticCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(result.TechnicalDetail))
        {
            return;
        }

        report.Append(Loc.Tr("Diagnostics_TechnicalDetails")).Append(":\n\n");
        AppendIndented(report, result.TechnicalDetail);
    }

    private static void AppendFix(StringBuilder report, DiagnosticCheckResult result)
    {
        if (result.Fix == null)
        {
            return;
        }

        report.Append(Loc.Tr("Diagnostics_SuggestedFix")).Append(":\n\n");
        report.Append(Scrub(result.Fix.Description)).Append('\n');

        foreach (string requirement in Requirements(result.Fix))
        {
            report.Append("- ").Append(requirement).Append('\n');
        }

        report.Append('\n');

        if (!string.IsNullOrWhiteSpace(result.Fix.Command))
        {
            AppendIndented(report, result.Fix.Command);
        }
    }

    private static IEnumerable<string> Requirements(DiagnosticFix fix)
    {
        if (fix.RequiresElevation)
        {
            yield return Loc.Tr("Diagnostics_RequiresElevation");
        }

        if (fix.RequiresLogout)
        {
            yield return Loc.Tr("Diagnostics_RequiresLogout");
        }

        if (fix.RequiresReconnect)
        {
            yield return Loc.Tr("Diagnostics_RequiresReconnect");
        }
    }

    /// <summary>
    /// Writes a block indented by four spaces rather than fenced, so a stray backtick in an
    /// error message cannot break out of the block when the report is pasted somewhere.
    /// </summary>
    private static void AppendIndented(StringBuilder report, string text)
    {
        foreach (string line in Scrub(text).Split('\n'))
        {
            report.Append("    ").Append(line.TrimEnd('\r')).Append('\n');
        }

        report.Append('\n');
    }

    /// <summary>Makes a value safe for a table cell: no pipes, no line breaks.</summary>
    private static string Cell(string value)
    {
        return Scrub(value)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string Scrub(string value) => DiagnosticReportSanitizer.Scrub(value) ?? string.Empty;
}
