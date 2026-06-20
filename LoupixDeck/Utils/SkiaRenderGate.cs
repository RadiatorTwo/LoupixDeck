namespace LoupixDeck.Utils;

/// <summary>
/// Global serialization gate for direct SkiaSharp work (drawing into an
/// <c>SKCanvas</c>/<c>SKBitmap</c> and reading its pixels).
///
/// SkiaSharp objects — and Skia's internal font/glyph caches touched by every
/// draw — are not designed for concurrent use across threads. The touch-button
/// render/convert pipeline is entered from several threads: the Avalonia UI
/// thread, the device send path, and threadpool continuations from
/// <c>async void</c> event handlers (exclusive-mode end, folder-state changes,
/// device-state restore) plus the <c>DynamicTextManager</c> idle timer. See
/// <c>docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md</c> (measure 1).
///
/// Every synchronous Skia draw/pixel-read that can run off the UI thread takes
/// this lock so no two ever overlap. Hold it only around CPU-bound Skia work —
/// never across an <c>await</c> or device I/O.
/// </summary>
public static class SkiaRenderGate
{
    /// <summary>Monitor object guarding all gated Skia operations.</summary>
    public static readonly object Sync = new();
}
