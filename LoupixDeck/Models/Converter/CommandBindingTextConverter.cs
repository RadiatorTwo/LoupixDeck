using System.Globalization;
using Avalonia.Data.Converters;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Shows a <see cref="CommandBinding"/> in a text box in the legacy syntax and reads what the user
/// types back the same way, for the editors that let a chain be typed by hand.
/// </summary>
public sealed class CommandBindingTextConverter : IValueConverter
{
    public static CommandBindingTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value as CommandBinding)?.ToLegacyString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => CommandBinding.FromLegacy(value as string ?? string.Empty);
}
