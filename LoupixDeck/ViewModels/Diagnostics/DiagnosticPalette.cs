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

    // Full-strength status colours, not the muted GitHub palette: a chip has to be loud enough
    // to spot from the other side of the room. The label is whatever contrasts with its own
    // fill - near-black on the green and the yellow, white on the red, which is dark enough.
    public static readonly IBrush PassFill = SolidColorBrush.Parse("#00C853");
    public static readonly IBrush WarningFill = SolidColorBrush.Parse("#FFC400");
    public static readonly IBrush FailFill = SolidColorBrush.Parse("#FF1F1F");
    public static readonly IBrush NeutralFill = SolidColorBrush.Parse("#9AA0A6");

    public static readonly IBrush PassBorder = SolidColorBrush.Parse("#00E676");
    public static readonly IBrush WarningBorder = SolidColorBrush.Parse("#FFD740");
    public static readonly IBrush FailBorder = SolidColorBrush.Parse("#FF5252");
    public static readonly IBrush NeutralBorder = SolidColorBrush.Parse("#B6BCC2");

    public static readonly IBrush PassText = SolidColorBrush.Parse("#062810");
    public static readonly IBrush WarningText = SolidColorBrush.Parse("#2A1D00");
    public static readonly IBrush FailText = Brushes.White;
    public static readonly IBrush NeutralText = Brushes.White;

    /// <summary>The dot inside a chip, on top of the saturated fill.</summary>
    public static readonly IBrush ChipDot = SolidColorBrush.Parse("#D9FFFFFF");

    public static readonly IBrush DarkChipDot = SolidColorBrush.Parse("#73000000");

    /// <summary>A counter of zero: the chip keeps its shape but not the loud colour.</summary>
    public static readonly IBrush EmptyFill = SolidColorBrush.Parse("#9AA0A6");

    public static readonly IBrush EmptyBorder = SolidColorBrush.Parse("#B6BCC2");

    /// <summary>The dot / text colour for a status.</summary>
    public static IBrush Dot(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => Pass,
        DiagnosticStatus.Warning => Warning,
        DiagnosticStatus.Fail => Fail,
        _ => Muted
    };

    /// <summary>The dot inside a chip: the label colour at reduced weight.</summary>
    public static IBrush Chip(DiagnosticStatus status) =>
        status is DiagnosticStatus.Warning or DiagnosticStatus.Pass ? DarkChipDot : ChipDot;

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
