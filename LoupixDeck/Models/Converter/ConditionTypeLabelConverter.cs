using LoupixDeck.Localization;
using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Maps a <see cref="ConditionType"/> to its user-facing label for the macro editor's
/// condition pickers, which would otherwise show the raw enum name.
/// </summary>
public class ConditionTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ConditionType type ? Label(type) : value?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Label(ConditionType type) => type switch
    {
        ConditionType.ProcessRunning => Loc.Tr("Condition_ProcessRunning"),
        ConditionType.ActiveWindowProcessIs => Loc.Tr("Condition_ActiveWindowProcessIs"),
        ConditionType.ActiveWindowTitleContains => Loc.Tr("Condition_ActiveWindowTitleContains"),
        ConditionType.Variable => Loc.Tr("Condition_Variable"),
        ConditionType.TriggerButtonReleased => Loc.Tr("Condition_TriggerButtonReleased"),
        _ => type.ToString()
    };
}
