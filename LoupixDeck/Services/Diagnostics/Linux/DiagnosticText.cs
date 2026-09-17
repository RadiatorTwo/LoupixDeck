using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>Localized wording for the diagnostic enums, shared by the page and the report.</summary>
public static class DiagnosticText
{
    /// <summary>The localized word for a status.</summary>
    public static string StatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => Loc.Tr("Diagnostics_StatusPass"),
        DiagnosticStatus.Warning => Loc.Tr("Diagnostics_StatusWarning"),
        DiagnosticStatus.Fail => Loc.Tr("Diagnostics_StatusFail"),
        DiagnosticStatus.Skipped => Loc.Tr("Diagnostics_StatusSkipped"),
        _ => Loc.Tr("Diagnostics_StatusUnknown")
    };

    /// <summary>The localized title of a category.</summary>
    public static string CategoryTitle(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.System => Loc.Tr("Diagnostics_CategorySystem"),
        DiagnosticCategory.Session => Loc.Tr("Diagnostics_CategorySession"),
        DiagnosticCategory.InputInjection => Loc.Tr("Diagnostics_CategoryInputInjection"),
        DiagnosticCategory.InputRecording => Loc.Tr("Diagnostics_CategoryInputRecording"),
        _ => category.ToString()
    };
}
