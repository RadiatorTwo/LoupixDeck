using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Localization;
using LoupixDeck.Models.Layers;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Maps a <see cref="TextSource"/> to its user-facing label for the text layer's source picker.
/// </summary>
public class TextSourceLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TextSource source ? Label(source) : value?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Label(TextSource source) => source switch
    {
        TextSource.Static => Loc.Tr("TextSource_Static"),
        TextSource.DialLabel => Loc.Tr("TextSource_DialLabel"),
        TextSource.DialValue => Loc.Tr("TextSource_DialValue"),
        _ => source.ToString()
    };
}
