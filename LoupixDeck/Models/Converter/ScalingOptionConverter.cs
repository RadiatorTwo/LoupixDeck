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
                BitmapHelper.ScalingOption.None => "None - Image shown as is in full resolution",
                BitmapHelper.ScalingOption.Fill => "Fill - The image fills the screen, the aspect ratio may be lost",
                BitmapHelper.ScalingOption.Fit => "Fit - The image is scaled to be completely visible, the aspect ratio is retained",
                BitmapHelper.ScalingOption.Stretch => "Stretch - The image is distorted to fill the screen completely",
                BitmapHelper.ScalingOption.Tile => "Tile - The image is displayed several times next to each other/repeatedly",
                BitmapHelper.ScalingOption.Center => "Center - The image is displayed centered without scaling",
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