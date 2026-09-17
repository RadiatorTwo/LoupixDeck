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

    /// <summary>
    /// Sort weight: what is wrong comes first, what is fine comes last. Used for the check list
    /// and to pick which check a category opens on.
    /// </summary>
    public static int Rank(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Fail => 0,
        DiagnosticStatus.Warning => 1,
        DiagnosticStatus.Unknown => 2,
        DiagnosticStatus.Pass => 3,
        _ => 4
    };

    /// <summary>The worst status in a set, or <see cref="DiagnosticStatus.Pass"/> when it is empty.</summary>
    public static DiagnosticStatus Worst(IEnumerable<DiagnosticStatus> statuses)
    {
        DiagnosticStatus worst = DiagnosticStatus.Pass;

        foreach (DiagnosticStatus status in statuses)
        {
            if (Rank(status) < Rank(worst))
            {
                worst = status;
            }
        }

        return worst;
    }

    /// <summary>The localized title of a category.</summary>
    public static string CategoryTitle(DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.System => Loc.Tr("Diagnostics_CategorySystem"),
        DiagnosticCategory.Session => Loc.Tr("Diagnostics_CategorySession"),
        DiagnosticCategory.DeviceAccess => Loc.Tr("Diagnostics_CategoryDeviceAccess"),
        DiagnosticCategory.InputInjection => Loc.Tr("Diagnostics_CategoryInputInjection"),
        DiagnosticCategory.InputRecording => Loc.Tr("Diagnostics_CategoryInputRecording"),
        DiagnosticCategory.Plugins => Loc.Tr("Diagnostics_CategoryPlugins"),
        DiagnosticCategory.Installation => Loc.Tr("Diagnostics_CategoryInstallation"),
        _ => category.ToString()
    };
}
