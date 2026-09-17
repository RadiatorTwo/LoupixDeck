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

    // Filled chips rather than a tint of the status colour: the old 12%-alpha fills with a 30%
    // border were washed out on both themes, and the bright dot colour as text had no contrast on
    // a white card. These are the same solid values as Border.pill in SettingsStyles.axaml, which
    // reads in Light and Dark alike.
    public static readonly IBrush PassFill = SolidColorBrush.Parse("#1F3D2A");
    public static readonly IBrush WarningFill = SolidColorBrush.Parse("#4A3A1A");
    public static readonly IBrush FailFill = SolidColorBrush.Parse("#4A1F22");
    public static readonly IBrush NeutralFill = SolidColorBrush.Parse("#2E2E2E");

    public static readonly IBrush PassBorder = SolidColorBrush.Parse("#2E6B45");
    public static readonly IBrush WarningBorder = SolidColorBrush.Parse("#7A5C2A");
    public static readonly IBrush FailBorder = SolidColorBrush.Parse("#7A2A30");
    public static readonly IBrush NeutralBorder = SolidColorBrush.Parse("#4A4A4A");

    public static readonly IBrush PassText = SolidColorBrush.Parse("#7BD89E");
    public static readonly IBrush WarningText = SolidColorBrush.Parse("#F0C878");
    public static readonly IBrush FailText = SolidColorBrush.Parse("#F08A8A");
    public static readonly IBrush NeutralText = SolidColorBrush.Parse("#C4C4C4");

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

    /// <summary>The label colour inside a badge, on top of <see cref="Fill"/>.</summary>
    public static IBrush Text(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => PassText,
        DiagnosticStatus.Warning => WarningText,
        DiagnosticStatus.Fail => FailText,
        _ => NeutralText
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
