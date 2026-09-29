using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LoupixDeck.Utils;
using SkiaSharp;
using Svg.Skia;
using AvaloniaColor = Avalonia.Media.Color;

namespace LoupixDeck.Services.IconPacks;

/// <summary>
/// Decodes small preview bitmaps of pack icons for one symbol picker session.
/// </summary>
/// <remarks>
/// Nothing is decoded up front: a cell asks for its thumbnail the first time the virtualized grid
/// shows it. Requests are served newest first, so after a fast scroll the rows now on screen win
/// over the ones scrolled past. Decoded thumbnails are kept in a small LRU cache that is disposed
/// with the loader when the picker closes.
/// </remarks>
public sealed class IconThumbnailLoader : IDisposable
{
    /// <summary>Longest edge of a thumbnail in pixels; enough for a 48 px cell on a 2x display.</summary>
    public const int ThumbnailSize = 96;

    private const int CacheCapacity = 1000;

    private sealed record PendingLoad(IconPackEntry Entry, Action<Bitmap> OnLoaded);

    private readonly ConcurrentStack<PendingLoad> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _cacheGate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Bitmap)>> _cache = [];
    private readonly LinkedList<(string Key, Bitmap Bitmap)> _lru = new();
    private readonly SKColor _monochromeColor;

    /// <param name="monochromeColor">
    /// Color single-color icons are drawn in, so black line icons stay visible on a dark theme.
    /// </param>
    public IconThumbnailLoader(AvaloniaColor monochromeColor)
    {
        _monochromeColor = new SKColor(monochromeColor.R, monochromeColor.G, monochromeColor.B, monochromeColor.A);

        int workers = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        for (int i = 0; i < workers; i++)
            _ = Task.Run(WorkAsync);
    }

    /// <summary>
    /// Queues <paramref name="entry"/>; <paramref name="onLoaded"/> runs on the UI thread with the
    /// thumbnail, or with null when the file cannot be decoded. A cached thumbnail is returned at once.
    /// </summary>
    public Bitmap Request(IconPackEntry entry, Action<Bitmap> onLoaded)
    {
        if (TryGetCached(entry.Key, out Bitmap cached))
            return cached;

        _pending.Push(new PendingLoad(entry, onLoaded));
        _signal.Release();
        return null;
    }

    /// <summary>Drops requests not yet started, e.g. after the filter replaced the grid's cells.</summary>
    public void ClearPending() => _pending.Clear();

    private async Task WorkAsync()
    {
        CancellationToken token = _cancellation.Token;
        try
        {
            while (true)
            {
                await _signal.WaitAsync(token);
                if (!_pending.TryPop(out PendingLoad request))
                    continue;

                if (!TryGetCached(request.Entry.Key, out Bitmap bitmap))
                {
                    bitmap = Decode(request.Entry);
                    if (token.IsCancellationRequested)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    if (bitmap != null)
                        bitmap = AddToCache(request.Entry.Key, bitmap);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                        request.OnLoaded(bitmap);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The picker closed.
        }
    }

    private Bitmap Decode(IconPackEntry entry)
    {
        try
        {
            using SKBitmap source = entry.Kind == IconEntryKind.SvgFile
                ? RenderSvg(entry.FullPath)
                : DecodeRaster(entry.FullPath);

            if (source == null)
                return null;

            using SKBitmap colored = IconColorAnalysis.IsMonochrome(source) ? Recolor(source) : null;
            return ToAvalonia(colored ?? source);
        }
        catch (Exception ex)
        {
            // A broken file shows an empty cell; it must not stop the other thumbnails.
            Console.WriteLine($"[IconPacks] Thumbnail of '{entry.FullPath}' failed: {ex.Message}");
            return null;
        }
    }

    private static SKBitmap RenderSvg(string path)
    {
        using SKSvg svg = new();
        SKPicture picture = svg.Load(path);
        if (picture == null)
            return null;

        SKRect bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        float scale = ThumbnailSize / Math.Max(bounds.Width, bounds.Height);
        SKBitmap bitmap = new(new SKImageInfo(
            Math.Max(1, (int)Math.Round(bounds.Width * scale)),
            Math.Max(1, (int)Math.Round(bounds.Height * scale)),
            SKColorType.Bgra8888, SKAlphaType.Premul));

        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        return bitmap;
    }

    private static SKBitmap DecodeRaster(string path)
    {
        using SKCodec codec = SKCodec.Create(path);
        if (codec == null)
            return null;

        SKImageInfo info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            return null;

        // Let the codec downscale while decoding where it can (JPEG, WebP); PNG decodes full size.
        float scale = Math.Min(1f, (float)ThumbnailSize / Math.Max(info.Width, info.Height));
        SKSizeI scaled = codec.GetScaledDimensions(scale);
        SKImageInfo target = new(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        SKBitmap decoded = new(target);
        SKCodecResult result = codec.GetPixels(target, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            decoded = SKBitmap.Decode(codec);
            if (decoded == null)
                return null;
        }

        int longest = Math.Max(decoded.Width, decoded.Height);
        if (longest <= ThumbnailSize)
            return decoded;

        float fit = (float)ThumbnailSize / longest;
        SKImageInfo resized = new(
            Math.Max(1, (int)Math.Round(decoded.Width * fit)),
            Math.Max(1, (int)Math.Round(decoded.Height * fit)),
            SKColorType.Bgra8888, SKAlphaType.Premul);

        SKBitmap resizedBitmap = decoded.Resize(resized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        decoded.Dispose();
        return resizedBitmap;
    }

    private SKBitmap Recolor(SKBitmap source)
    {
        SKBitmap bitmap = new(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using SKCanvas canvas = new(bitmap);
        using SKColorFilter filter = SKColorFilter.CreateBlendMode(_monochromeColor, SKBlendMode.SrcIn);
        using SKPaint paint = new() { ColorFilter = filter };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default, paint);
        return bitmap;
    }

    private static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        // Normalize the layout so the pixel format handed to Avalonia is always known.
        using SKBitmap normalized = bitmap.ColorType == SKColorType.Bgra8888 && bitmap.AlphaType == SKAlphaType.Premul
            ? null
            : bitmap.Copy(SKColorType.Bgra8888);

        SKBitmap pixels = normalized ?? bitmap;
        if (pixels == null || pixels.GetPixels() == IntPtr.Zero)
            return null;

        // The constructor copies the pixels, so the Skia bitmap can be disposed afterwards.
        Bitmap result = new(
            PixelFormat.Bgra8888,
            pixels.AlphaType == SKAlphaType.Unpremul ? AlphaFormat.Unpremul : AlphaFormat.Premul,
            pixels.GetPixels(),
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            pixels.RowBytes);
        GC.KeepAlive(pixels);
        return result;
    }

    private bool TryGetCached(string key, out Bitmap bitmap)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, Bitmap Bitmap)> node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>Caches a thumbnail and returns the cached instance (another worker may have won).</summary>
    private Bitmap AddToCache(string key, Bitmap bitmap)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, Bitmap Bitmap)> existing))
            {
                bitmap.Dispose();
                return existing.Value.Bitmap;
            }

            _cache[key] = _lru.AddFirst((key, bitmap));

            // Evicted thumbnails are not disposed: a realized cell may still show one, and disposing
            // a bitmap an Image is rendering crashes the renderer. The GC reclaims them once unused.
            while (_lru.Count > CacheCapacity)
            {
                _cache.Remove(_lru.Last!.Value.Key);
                _lru.RemoveLast();
            }
        }

        return bitmap;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _pending.Clear();

        // Only dropped, not disposed, for the same reason as eviction: the closing window may still
        // render a frame with them.
        lock (_cacheGate)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }
}
