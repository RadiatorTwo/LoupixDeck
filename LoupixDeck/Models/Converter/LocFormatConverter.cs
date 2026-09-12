using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Localization;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Formats a bound value into a localized template resolved by key at runtime, replacing XAML
/// <c>StringFormat</c>, whose literal template cannot be translated. The converter parameter is the
/// translation key whose value is a composite format string, for example
/// <c>Text="{Binding Count, Converter={StaticResource LocFmt}, ConverterParameter=Fmt_CommandCount}"</c>
/// with <c>Fmt_CommandCount</c> holding <c>"{0} commands"</c>.
/// </summary>
public sealed class LocFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = parameter as string;
        if (string.IsNullOrEmpty(key))
        {
            return value?.ToString();
        }

        string template = LocalizationManager.Instance[key];

        try
        {
            return string.Format(culture, template, value);
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"[Localization] Template '{key}' is not a valid composite format string: {ex.Message}");
            return template;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
