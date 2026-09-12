using LoupixDeck.Localization;
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
        StateTransitionKind.Stay => Loc.Tr("Transition_Stay"),
        StateTransitionKind.Next => Loc.Tr("Transition_Next"),
        StateTransitionKind.Previous => Loc.Tr("Transition_Previous"),
        StateTransitionKind.Specific => Loc.Tr("Transition_Specific"),
        StateTransitionKind.ResetToDefault => Loc.Tr("Transition_ResetToDefault"),
        _ => kind.ToString()
    };
}
