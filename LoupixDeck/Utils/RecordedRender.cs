using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Draws a device image through a recording instead of straight into a bitmap: the drawing is
/// captured as an <see cref="SKPicture"/> and then rasterized at device size, which gives the same
/// pixels. The picture is kept with the bitmap, so the on-screen device view can play the very
/// same drawing back at its own resolution (issue #251) - sharp, and without calling a plugin's
/// render code a second time.
/// </summary>
/// <remarks>Use it like a bitmap and canvas: draw on <see cref="Canvas"/>, then take the result
/// from <see cref="Finish"/>, both under <see cref="SkiaRenderGate"/>.Sync.</remarks>
internal sealed class RecordedRender : IDisposable
{
    private readonly SKPictureRecorder _recorder = new();
    private readonly int _width;
    private readonly int _height;

    public RecordedRender(int width, int height)
    {
        _width = width;
        _height = height;
        Canvas = _recorder.BeginRecording(new SKRect(0, 0, width, height));
    }

    /// <summary>The canvas to draw on, in device pixels. Valid until <see cref="Finish"/>.</summary>
    public SKCanvas Canvas { get; }

    /// <summary>Ends the recording and returns the device-sized bitmap drawn from it.</summary>
    public SKBitmap Finish()
    {
        SKPicture picture = _recorder.EndRecording();

        var bitmap = new SKBitmap(_width, _height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawPicture(picture);
        }

        BitmapHelper.RememberRecording(bitmap, picture);
        return bitmap;
    }

    public void Dispose()
    {
        _recorder.Dispose();
    }
}
