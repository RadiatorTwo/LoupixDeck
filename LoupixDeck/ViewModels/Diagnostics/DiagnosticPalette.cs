using Avalonia.Media;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>
/// The status colours of the diagnostics page. Fixed values rather than theme resources,
/// the same way the existing status pills in SettingsStyles.axaml are: a pass has to read as a
/// pass in both themes, and a theme's accent is not that.
/// </summary>
public static class DiagnosticPalette
{
    public static readonly IBrush Pass = SolidColorBrush.Parse("#3FB950");
    public static readonly IBrush Warning = SolidColorBrush.Parse("#E3B341");
    public static readonly IBrush Fail = SolidColorBrush.Parse("#F85149");
    public static readonly IBrush Muted = SolidColorBrush.Parse("#A8A8A8");
    public static readonly IBrush Off = SolidColorBrush.Parse("#4A4A4A");

    public static readonly IBrush PassFill = SolidColorBrush.Parse("#1F3FB950");
    public static readonly IBrush WarningFill = SolidColorBrush.Parse("#24D29922");
    public static readonly IBrush FailFill = SolidColorBrush.Parse("#1FF85149");
    public static readonly IBrush NeutralFill = Brushes.Transparent;

    public static readonly IBrush PassBorder = SolidColorBrush.Parse("#4D3FB950");
    public static readonly IBrush WarningBorder = SolidColorBrush.Parse("#59D29922");
    public static readonly IBrush FailBorder = SolidColorBrush.Parse("#4DF85149");
    public static readonly IBrush NeutralBorder = SolidColorBrush.Parse("#24FFFFFF");

    /// <summary>The dot / text colour for a status.</summary>
    public static IBrush Dot(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => Pass,
        DiagnosticStatus.Warning => Warning,
        DiagnosticStatus.Fail => Fail,
        _ => Muted
    };

    /// <summary>The badge background for a status.</summary>
    public static IBrush Fill(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => PassFill,
        DiagnosticStatus.Warning => WarningFill,
        DiagnosticStatus.Fail => FailFill,
        _ => NeutralFill
    };

    /// <summary>The badge border for a status.</summary>
    public static IBrush Border(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => PassBorder,
        DiagnosticStatus.Warning => WarningBorder,
        DiagnosticStatus.Fail => FailBorder,
        _ => NeutralBorder
    };
}
