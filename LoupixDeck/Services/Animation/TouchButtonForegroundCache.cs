using LoupixDeck.Models;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Keeps each key's layers pre-rendered on a transparent background, so a source pushing whole
/// panel frames can blit them over a video wallpaper instead of re-rendering them every frame.
///
/// This exists because of the frame budget. A full-panel serial write measures ~27 ms on a 480×270
/// panel, leaving roughly 6 ms of a 33 ms frame at 30 fps — enough for a handful of blits, nowhere
/// near enough to re-run layer rendering, text layout and plugin callbacks for fifteen keys. One
/// entry is a key-sized bitmap (~32 KB at 90×90), so the whole cache is well under a megabyte.
///
/// Lifetime: entries are created and read on the render thread while it holds
/// <see cref="SkiaRenderGate"/>.Sync, and every mutation here takes that same gate. That is what
/// makes disposal safe — an entry can never be freed while a frame is compositing with it. Unlike
/// <see cref="IAnimatedImageCache"/>, these bitmaps are never handed to the UI or stored on a
/// layer, so nothing outside this class can hold one past the gate.
/// </summary>
public sealed class TouchButtonForegroundCache : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly Dictionary<int, SKBitmap> _entries = [];
    private bool _disposed;

    public TouchButtonForegroundCache(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>
    /// The key's pre-rendered layers, rendering them on first use. Returns null for a key with
    /// nothing to draw, so the caller can skip the blit entirely. Call on the render thread; it
    /// takes the shared Skia gate (re-entrantly, when the caller already holds it).
    /// </summary>
    public SKBitmap Get(TouchButton button)
    {
        if (button == null) return null;

        lock (SkiaRenderGate.Sync)
        {
            if (_disposed) return null;
            if (_entries.TryGetValue(button.Index, out var cached)) return cached;

            // Nothing to draw: cache the absence too, so an empty key does not re-render forever.
            var foreground = button.Layers is { Count: > 0 }
                ? BitmapHelper.RenderTouchButtonForeground(button, _width, _height)
                : null;

            _entries[button.Index] = foreground;
            return foreground;
        }
    }

    /// <summary>Drops one key's entry, so its next frame re-renders it.</summary>
    public void Invalidate(int index)
    {
        lock (SkiaRenderGate.Sync)
        {
            if (_entries.Remove(index, out var stale)) stale?.Dispose();
        }
    }

    /// <summary>Drops every entry — what a page change needs.</summary>
    public void InvalidateAll()
    {
        lock (SkiaRenderGate.Sync)
        {
            foreach (var entry in _entries.Values) entry?.Dispose();
            _entries.Clear();
        }
    }

    public void Dispose()
    {
        lock (SkiaRenderGate.Sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values) entry?.Dispose();
            _entries.Clear();
        }
    }
}
