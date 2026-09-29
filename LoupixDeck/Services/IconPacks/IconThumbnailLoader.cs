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
/// Only runs while the picker is open: when a pack is shown, all of its icons are queued top to
/// bottom, and a cell the grid shows before its turn is moved to the front. Finished thumbnails are
/// handed to the UI in batches rather than one dispatcher call each, so decoding does not compete
/// with scrolling. Everything is released when the picker closes; nothing is kept for the app.
/// </remarks>
public sealed class IconThumbnailLoader : IDisposable
{
    /// <summary>Longest edge of a thumbnail in pixels; the grid shows 44 px, so this covers ~150% scaling.</summary>
    public const int ThumbnailSize = 64;

    /// <summary>How often finished thumbnails are handed to the UI.</summary>
    private static readonly TimeSpan DeliveryInterval = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentStack<IconPackEntry> _pending = new();
    private readonly ConcurrentQueue<(string Key, Bitmap Bitmap)> _finished = new();
    private readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _started = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly DispatcherTimer _deliveryTimer;
    private readonly SKColor _monochromeColor;

    /// <summary>Raised on the UI thread with thumbnails finished since the last batch.</summary>
    public event Action<IReadOnlyList<(string Key, Bitmap Bitmap)>> ThumbnailsLoaded;

    /// <param name="monochromeColor">
    /// Color single-color icons are drawn in, so black line icons stay visible on a dark theme.
    /// </param>
    public IconThumbnailLoader(AvaloniaColor monochromeColor)
    {
        _monochromeColor = new SKColor(monochromeColor.R, monochromeColor.G, monochromeColor.B, monochromeColor.A);

        _deliveryTimer = new DispatcherTimer(DeliveryInterval, DispatcherPriority.Background, (_, _) => Deliver());
        _deliveryTimer.Start();

        int workers = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        for (int i = 0; i < workers; i++)
            _ = Task.Run(WorkAsync);
    }

    /// <summary>
    /// Queues every icon of a pack, first entry first, replacing what an earlier pack left queued.
    /// </summary>
    public void LoadAll(IReadOnlyList<IconPackEntry> entries)
    {
        _pending.Clear();

        // A stack serves the last push first, so push bottom-up to decode top-down.
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (!_cache.ContainsKey(entries[i].Key))
                _pending.Push(entries[i]);
        }

        _signal.Release(Math.Max(1, _pending.Count));
    }

    /// <summary>
    /// The thumbnail if it is already decoded; otherwise moves the icon to the front of the queue
    /// and returns null. It arrives later through <see cref="ThumbnailsLoaded"/>.
    /// </summary>
    public Bitmap GetOrPrioritize(IconPackEntry entry)
    {
        if (_cache.TryGetValue(entry.Key, out Bitmap cached))
            return cached;

        _pending.Push(entry);
        _signal.Release();
        return null;
    }

    private async Task WorkAsync()
    {
        CancellationToken token = _cancellation.Token;
        try
        {
            while (true)
            {
                await _signal.WaitAsync(token);
                if (!_pending.TryPop(out IconPackEntry entry))
                    continue;

                // An icon can be queued twice (bulk + prioritized); decode it once.
                if (!_started.TryAdd(entry.Key, 0))
                    continue;

                Bitmap bitmap = Decode(entry);
                if (token.IsCancellationRequested)
                {
                    bitmap?.Dispose();
                    return;
                }

                if (bitmap == null)
                    continue;

                _cache[entry.Key] = bitmap;
                _finished.Enqueue((entry.Key, bitmap));
            }
        }
        catch (OperationCanceledException)
        {
            // The picker closed.
        }
    }

    private void Deliver()
    {
        if (_finished.IsEmpty)
            return;

        List<(string Key, Bitmap Bitmap)> batch = [];
        while (_finished.TryDequeue(out (string Key, Bitmap Bitmap) item))
            batch.Add(item);

        ThumbnailsLoaded?.Invoke(batch);
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

    /// <summary>
    /// Stops the workers and frees every thumbnail. Called once the picker window has closed, so no
    /// realized cell renders them any more.
    /// </summary>
    public void Dispose()
    {
        _cancellation.Cancel();
        _deliveryTimer.Stop();
        _pending.Clear();
        _finished.Clear();
        ThumbnailsLoaded = null;

        foreach (Bitmap bitmap in _cache.Values)
            bitmap.Dispose();
        _cache.Clear();
    }
}
