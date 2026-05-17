using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LoupixDeck.Models.Converter;

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Only the selected RadioButton fires ConvertBack with true; unselected
        // ones fire with false and must not overwrite the bound enum.
        if (value is true && parameter != null)
        {
            if (parameter.GetType() == targetType) return parameter;
            if (targetType.IsEnum)
            {
                return Enum.TryParse(targetType, parameter.ToString(), out var parsed)
                    ? parsed
                    : BindingOperations.DoNothing;
            }
        }
        return BindingOperations.DoNothing;
    }
}