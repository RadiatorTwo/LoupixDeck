using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LoupixDeck.Utils
{
    public static class ImageSharpExtensions
    {
        public static Bitmap ToAvaloniaBitmap(this Image<Rgba32> image)
        {
            using var ms = new MemoryStream();
            image.SaveAsPng(ms); // PNG bewahrt Transparenz
            ms.Position = 0;

            return new Bitmap(ms);
        }
    }
}
