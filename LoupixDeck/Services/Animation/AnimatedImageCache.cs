using System.Collections.Concurrent;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="IAnimatedImageCache"/>
/// <remarks>
/// Lifetime ownership is deliberate. A decoded animation's frames are handed out as shared
/// <c>SKBitmap</c> references: <see cref="ButtonAnimationSource"/> swaps them into
/// <see cref="Models.Layers.ImageLayer.CachedImage"/> on every device, the controller composites them
/// on a redraw, and the editor binds them for its preview. Those references outlive any single render
/// and live on threads this cache can't see, so disposing a frame while it is still referenced is a
/// use-after-free (an access violation in <c>SKBitmap</c>). Proving "no live reference" across the
/// editor + every page + the render thread is fragile, so the cache instead keeps each decoded
/// animation alive for its whole lifetime (it is a root singleton ≈ app lifetime) and frees the
/// native pixels only in <see cref="Clear"/>/<see cref="Dispose"/> at shutdown, when nothing renders.
///
/// Memory is bounded by the number of DISTINCT animations referenced during the session (each a
/// handful of 90×90 BGRA frames ≈ tens to low-hundreds of KB), which is modest for a deck config and
/// is reclaimed on restart. Re-importing a clip yields a new content-addressed key, so a replaced
/// animation lingers until restart — an acceptable trade for eliminating the crash.
/// </remarks>
public sealed class AnimatedImageCache(IAssetService assetService) : IAnimatedImageCache, IDisposable
{
    private readonly ConcurrentDictionary<string, DecodedAnimation> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public DecodedAnimation Get(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        if (_entries.TryGetValue(relativePath, out var existing))
            return existing;

        // Decode outside any lock — it is CPU-heavy. A racing decode of the same asset is resolved
        // by GetOrAdd keeping the first instance and disposing the loser below.
        var absolute = assetService.ResolveAbsolute(relativePath);
        var decoded = AnimatedImageDecoder.Decode(absolute);
        if (decoded == null) return null;

        var stored = _entries.GetOrAdd(relativePath, decoded);
        if (!ReferenceEquals(stored, decoded))
            decoded.Dispose(); // lost the race — the other instance won; ours was never handed out
        return stored;
    }

    /// <summary>
    /// Intentionally a no-op: frames are owned for the cache lifetime (see the class remarks) because
    /// disposing one still referenced by a layer/editor/render thread would be a use-after-free. The
    /// memory bound is the distinct-animation count, reclaimed by <see cref="Clear"/> at shutdown.
    /// </summary>
    public void Trim(IEnumerable<string> referencedRelativePaths)
    {
        // No-op by design — do not dispose live, shared frames.
    }

    public void Clear()
    {
        // Snapshot-and-clear, then dispose. Only safe at shutdown, when nothing renders.
        foreach (var key in _entries.Keys.ToArray())
        {
            if (_entries.TryRemove(key, out var anim))
                anim.Dispose();
        }
    }

    public void Dispose() => Clear();
}
