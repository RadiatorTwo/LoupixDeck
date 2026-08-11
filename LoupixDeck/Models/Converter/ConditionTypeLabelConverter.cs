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
        ConditionType.ProcessRunning => "Process running",
        ConditionType.ActiveWindowProcessIs => "Active window process is",
        ConditionType.ActiveWindowTitleContains => "Active window title contains",
        ConditionType.Variable => "Variable",
        ConditionType.TriggerButtonReleased => "Trigger button released",
        _ => type.ToString()
    };
}
