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
    /// <summary>A counter of zero: the chip keeps its shape but not the loud colour.</summary>
    public static readonly IBrush EmptyFill = SolidColorBrush.Parse("#8B949E");

    public static readonly IBrush EmptyBorder = SolidColorBrush.Parse("#A8B1BA");

    // Saturated chips: the status colour itself carries the fill, not a dark tint of it, so a
    // failure reads as red across the room. The label is the contrast partner of that fill -
    // white on green/red/grey, near-black on the yellow, which white would wash out.
    public static readonly IBrush PassFill = SolidColorBrush.Parse("#2EA043");
    public static readonly IBrush WarningFill = SolidColorBrush.Parse("#D29922");
    public static readonly IBrush FailFill = SolidColorBrush.Parse("#DA3633");
    public static readonly IBrush NeutralFill = SolidColorBrush.Parse("#6E7681");

    public static readonly IBrush PassBorder = SolidColorBrush.Parse("#3FB950");
    public static readonly IBrush WarningBorder = SolidColorBrush.Parse("#E3B341");
    public static readonly IBrush FailBorder = SolidColorBrush.Parse("#F85149");
    public static readonly IBrush NeutralBorder = SolidColorBrush.Parse("#8B949E");

    public static readonly IBrush PassText = Brushes.White;
    public static readonly IBrush WarningText = SolidColorBrush.Parse("#2A1D00");
    public static readonly IBrush FailText = Brushes.White;
    public static readonly IBrush NeutralText = Brushes.White;

    /// <summary>The dot inside a chip, on top of the saturated fill.</summary>
    public static readonly IBrush ChipDot = SolidColorBrush.Parse("#D9FFFFFF");

    public static readonly IBrush WarningChipDot = SolidColorBrush.Parse("#732A1D00");

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
        status == DiagnosticStatus.Warning ? WarningChipDot : ChipDot;

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
