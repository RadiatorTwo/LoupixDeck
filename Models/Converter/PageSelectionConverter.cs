using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Utils;

namespace LoupixDeck.Models.Converter;

public class PageSelectionConverter : IMultiValueConverter
{
    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Count < 2) return AppColors.ButtonDefault;

        var isSelected = values[0] is true;

        return isSelected ? AppColors.ButtonSelected : AppColors.ButtonDefault;
    }
}
