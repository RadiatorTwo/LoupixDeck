using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Collections.Generic;

namespace LoupixDeck.Models.Converter;

public class PageSelectionConverter : IMultiValueConverter
{
    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Count < 2) return Brushes.Gray;

        var isSelected = values[0] is bool selected && selected;

        return isSelected ? Brushes.DarkGray : Brushes.Gray;
    }
}
