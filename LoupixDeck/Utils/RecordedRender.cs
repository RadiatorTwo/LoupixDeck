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

        var bitmap = new RecordedBitmap(_width, _height, picture);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawPicture(picture);
        }

        BitmapHelper.RememberRecording(bitmap);
        return bitmap;
    }

    public void Dispose()
    {
        _recorder.Dispose();
    }
}

/// <summary>
/// A bitmap rasterized from a recording, which owns that recording and frees it with itself. The
/// picture keeps its own copy of every mutable bitmap drawn into it, so leaving it to the
/// finalizer let native memory pile up with every strip, wheel and plugin redraw.
/// </summary>
internal sealed class RecordedBitmap(int width, int height, SKPicture picture) : SKBitmap(width, height)
{
    /// <summary>The drawing this bitmap was rasterized from; null once the bitmap is disposed.</summary>
    /// <remarks>Read it under <see cref="SkiaRenderGate"/>.Sync: disposal clears it under the same gate.</remarks>
    public SKPicture Picture { get; private set; } = picture;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Under the gate, so a preview recipe or a render already playing the picture back
            // never sees it freed halfway through.
            lock (SkiaRenderGate.Sync)
            {
                Picture?.Dispose();
                Picture = null;
            }
        }

        base.Dispose(disposing);
    }
}
