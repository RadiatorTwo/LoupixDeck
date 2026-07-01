using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LoupixDeck.Setup.ViewModels;

namespace LoupixDeck.Setup.Converters;

/// <summary>
/// Maps a <see cref="StepState"/> to the brush used for its timeline glyph. Colours mirror the palette
/// declared in <c>App.axaml</c> (success / accent / danger / inactive).
/// </summary>
public sealed class StepStateToBrushConverter : IValueConverter
{
    public static readonly StepStateToBrushConverter Instance = new();

    private static readonly IBrush Done = new SolidColorBrush(Color.Parse("#4CAF6A"));
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#3D9BFF"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#E05656"));
    private static readonly IBrush Pending = new SolidColorBrush(Color.Parse("#5A5A5A"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            StepState.Done => Done,
            StepState.Active => Active,
            StepState.Failed => Failed,
            _ => Pending
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
