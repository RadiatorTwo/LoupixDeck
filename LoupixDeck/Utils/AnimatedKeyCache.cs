using LoupixDeck.Models.Layers;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// The static parts of a key with one animated layer, pre-rendered so a new animation frame only
/// has to draw that layer between them (see <see cref="BitmapHelper.RenderTouchButtonAroundLayer"/>):
/// everything below it — background, wallpaper and lower layers — and everything above it — the
/// upper layers and the folder badge.
/// </summary>
/// <remarks>
/// The bitmaps are built, read and freed under <see cref="SkiaRenderGate"/>.Sync.
/// <see cref="Invalidate"/> may be called from any thread.
/// </remarks>
public sealed class AnimatedKeyCache : IDisposable
{
    private int _generation;

    internal SKBitmap Under;
    internal SKBitmap Over;
    internal bool Disposed;

    // What the bitmaps were built for; any difference rebuilds them.
    internal int BuiltGeneration = -1;
    internal LayerBase Split;
    internal SKBitmap Wallpaper;
    internal double WallpaperOpacity;
    internal SKRectI? WallpaperSource;
    internal bool FolderBadge;
    internal LoupixDeck.PluginSdk.AdjustmentValue? Value;
    internal int Width;
    internal int Height;

    internal int Generation => Volatile.Read(ref _generation);

    /// <summary>Drops the cached parts on the next frame — the key itself changed.</summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    public void Dispose()
    {
        lock (SkiaRenderGate.Sync)
        {
            Disposed = true;
            Under?.Dispose();
            Over?.Dispose();
            Under = null;
            Over = null;
        }
    }
}
