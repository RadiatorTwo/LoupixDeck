using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LoupixDeck.Models.Converter;

public class BoolToBrushConverter : IValueConverter
{
    public IBrush SelectedBrush   { get; set; }
    public IBrush UnselectedBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected && targetType == typeof(IBrush))
            return isSelected ? SelectedBrush : UnselectedBrush;
        return UnselectedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}