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
    // The list dots use the same full-strength colours as the chips, so a row and its badge
    // never show two shades of the same status.
    public static readonly IBrush Pass = SolidColorBrush.Parse("#00C853");
    public static readonly IBrush Warning = SolidColorBrush.Parse("#FFB300");
    public static readonly IBrush Fail = SolidColorBrush.Parse("#FF1F1F");
    public static readonly IBrush Muted = SolidColorBrush.Parse("#9AA0A6");

    // Chip design: a loud fill, a darker shade of that same fill as the border so the chip has
    // a hard edge on white and on the dark surface alike, and near-black ink on top. Ink rather
    // than white, because white on the grey chip was mush - and one ink colour keeps the five
    // chips a set rather than five separate decisions.
    public static readonly IBrush PassFill = SolidColorBrush.Parse("#29D95F");
    public static readonly IBrush WarningFill = SolidColorBrush.Parse("#FFC400");
    public static readonly IBrush FailFill = SolidColorBrush.Parse("#FF5A4D");
    public static readonly IBrush NeutralFill = SolidColorBrush.Parse("#C7CDD3");

    public static readonly IBrush PassBorder = SolidColorBrush.Parse("#17A845");
    public static readonly IBrush WarningBorder = SolidColorBrush.Parse("#C79400");
    public static readonly IBrush FailBorder = SolidColorBrush.Parse("#C62828");
    public static readonly IBrush NeutralBorder = SolidColorBrush.Parse("#8E979F");

    public static readonly IBrush PassText = SolidColorBrush.Parse("#06280F");
    public static readonly IBrush WarningText = SolidColorBrush.Parse("#2A1D00");
    public static readonly IBrush FailText = SolidColorBrush.Parse("#2E0A06");
    public static readonly IBrush NeutralText = SolidColorBrush.Parse("#1F2429");

    /// <summary>A counter of zero: the chip keeps its shape but not the loud colour.</summary>
    public static readonly IBrush EmptyFill = SolidColorBrush.Parse("#C7CDD3");

    public static readonly IBrush EmptyBorder = SolidColorBrush.Parse("#8E979F");

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
