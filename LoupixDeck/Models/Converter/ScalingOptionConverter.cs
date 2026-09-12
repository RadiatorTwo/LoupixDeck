using LoupixDeck.Localization;
using System.Globalization;
using Avalonia.Data.Converters;
using LoupixDeck.Utils;

namespace LoupixDeck.Models.Converter;

public class ScalingOptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BitmapHelper.ScalingOption option)
        {
            return option switch
            {
                BitmapHelper.ScalingOption.None => Loc.Tr("Scaling_None"),
                BitmapHelper.ScalingOption.Fill => Loc.Tr("Scaling_Fill"),
                BitmapHelper.ScalingOption.Fit => Loc.Tr("Scaling_Fit"),
                BitmapHelper.ScalingOption.Stretch => Loc.Tr("Scaling_Stretch"),
                BitmapHelper.ScalingOption.Tile => Loc.Tr("Scaling_Tile"),
                BitmapHelper.ScalingOption.Center => Loc.Tr("Scaling_Center"),
                //BitmapHelper.ScalingOption.CropToFill => "Crop to Fill - Like 'Fill', but with cropping instead of distortion",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not implemented.");
    }
}