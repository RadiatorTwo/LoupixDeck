using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Models;

namespace LoupixDeck.Models.Converter;

/// <summary>
/// Maps a <see cref="StateTransitionKind"/> to its user-facing label for the transition picker.
/// </summary>
public class TransitionKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is StateTransitionKind kind ? Label(kind) : value?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Label(StateTransitionKind kind) => kind switch
    {
        StateTransitionKind.Stay => "Stay in current state",
        StateTransitionKind.Next => "Go to next state",
        StateTransitionKind.Previous => "Go to previous state",
        StateTransitionKind.Specific => "Go to specific state",
        StateTransitionKind.ResetToDefault => "Reset to default state",
        _ => kind.ToString()
    };
}
