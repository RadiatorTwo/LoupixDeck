using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Shows a changing <see cref="SKBitmap"/> in an image control without allocating an Avalonia
/// bitmap for every change, which is what binding it through the SKBitmap converter does. The
/// pixels are copied into two buffers kept for as long as the size stays. They alternate because an
/// image control only redraws when its source changes, and so a copy never writes into the frame
/// that is on screen.
/// </summary>
/// <remarks>
/// Asking again for the source shown last returns the same buffer without copying, so several
/// properties may hand out the same preview. A source is never written to after it is shown here
/// (wallpaper bakes and poster frames are replaced, not edited). Use it on the UI thread.
/// </remarks>
public sealed class PreviewBitmapBuffer : IDisposable
{
    private readonly WriteableBitmap[] _buffers = new WriteableBitmap[2];
    private int _index;
    private SKBitmap _shownSource;
    private WriteableBitmap _shown;
    private bool _disposed;

    /// <summary>The buffer holding <paramref name="source"/>'s pixels; null for no source.</summary>
    public Bitmap Show(SKBitmap source)
    {
        if (ReferenceEquals(source, _shownSource) && _shown != null)
            return _shown;

        _shownSource = null;
        _shown = null;
        if (_disposed || source is not { IsNull: false } || source.Width <= 0 || source.Height <= 0)
            return null;

        _index ^= 1;
        WriteableBitmap target = _buffers[_index];
        if (target == null || target.PixelSize.Width != source.Width || target.PixelSize.Height != source.Height)
        {
            target?.Dispose();
            target = new WriteableBitmap(new PixelSize(source.Width, source.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            _buffers[_index] = target;
        }

        bool copied;
        using (ILockedFramebuffer frame = target.Lock())
        {
            // Under the render gate: a wallpaper bake is retired and disposed under it.
            lock (SkiaRenderGate.Sync)
            {
                SKImageInfo info = new(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using SKPixmap pixels = source.PeekPixels();
                copied = pixels != null && pixels.ReadPixels(info, frame.Address, frame.RowBytes, 0, 0);
            }
        }

        if (!copied) return null;

        _shownSource = source;
        _shown = target;
        return target;
    }

    public void Dispose()
    {
        _disposed = true;
        _shownSource = null;
        _shown = null;
        for (int i = 0; i < _buffers.Length; i++)
        {
            _buffers[i]?.Dispose();
            _buffers[i] = null;
        }
    }
}
