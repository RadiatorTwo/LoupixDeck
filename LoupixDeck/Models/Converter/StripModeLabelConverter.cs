using LoupixDeck.Localization;
using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Models;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Maps a <see cref="StripMode"/> to its user-facing name for the side-strip mode picker. The enum
/// names are persisted in the config and stay as they are; only the shown name says what the mode
/// does: dial labels, user tap zones, or a plugin.
/// </summary>
public class StripModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is StripMode mode ? Label(mode) : value?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Label(StripMode mode) => mode switch
    {
        StripMode.Segmented => Loc.Tr("StripMode_DialLabels"),
        StripMode.FreeDraw => Loc.Tr("StripMode_TapZones"),
        StripMode.PluginOverride => Loc.Tr("StripMode_PluginOverride"),
        _ => mode.ToString()
    };
}
