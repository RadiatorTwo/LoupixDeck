using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Maps a <see cref="MacroPlatform"/> to the operating system's name for the macro editor's
/// OS picker, so macOS reads "macOS" instead of the raw enum name.
/// </summary>
public sealed class MacroPlatformLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is MacroPlatform platform ? MacroCondition.PlatformName(platform) : value?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
