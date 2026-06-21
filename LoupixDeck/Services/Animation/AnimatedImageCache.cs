using LoupixDeck.Utils;

namespace LoupixDeck.Services.Animation;

/// <inheritdoc cref="IAnimatedImageCache"/>
public sealed class AnimatedImageCache : IAnimatedImageCache, IDisposable
{
    // ~32 KB per 90×90 BGRA frame; 64 MB ≈ 2000 frames. Comfortably covers many animated
    // buttons while bounding worst-case memory. Trim() normally keeps the live set far smaller.
    private const long MaxBytes = 64L * 1024 * 1024;

    private readonly IAssetService _assetService;
    private readonly object _gate = new();

    // Insertion/access order is tracked separately so we can evict least-recently-used entries.
    private readonly Dictionary<string, DecodedAnimation> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new(); // most-recently-used at the front
    private long _bytes;

    public AnimatedImageCache(IAssetService assetService)
    {
        _assetService = assetService;
    }

    public DecodedAnimation Get(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        lock (_gate)
        {
            if (_entries.TryGetValue(relativePath, out var existing))
            {
                Touch(relativePath);
                return existing;
            }
        }

        // Decode outside the lock — it is CPU-heavy and must not serialize other lookups.
        var absolute = _assetService.ResolveAbsolute(relativePath);
        var decoded = AnimatedImageDecoder.Decode(absolute);
        if (decoded == null) return null;

        lock (_gate)
        {
            // Another thread may have decoded the same asset while we were working — keep the
            // first one and discard our duplicate.
            if (_entries.TryGetValue(relativePath, out var raced))
            {
                decoded.Dispose();
                Touch(relativePath);
                return raced;
            }

            _entries[relativePath] = decoded;
            _lru.AddFirst(relativePath);
            _bytes += decoded.TotalBytes;
            EvictIfNeeded(keep: relativePath);
            return decoded;
        }
    }

    public void Trim(IEnumerable<string> referencedRelativePaths)
    {
        var keep = new HashSet<string>(
            referencedRelativePaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        List<DecodedAnimation> toDispose = null;
        lock (_gate)
        {
            foreach (var key in _entries.Keys.ToArray())
            {
                if (keep.Contains(key)) continue;
                Remove(key, ref toDispose);
            }
        }

        DisposeAll(toDispose);
    }

    public void Clear()
    {
        List<DecodedAnimation> toDispose;
        lock (_gate)
        {
            toDispose = _entries.Values.ToList();
            _entries.Clear();
            _lru.Clear();
            _bytes = 0;
        }

        DisposeAll(toDispose);
    }

    public void Dispose() => Clear();

    // ── helpers (caller holds _gate) ──────────────────────────────────────────

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddFirst(key);
    }

    private void EvictIfNeeded(string keep)
    {
        List<DecodedAnimation> toDispose = null;
        while (_bytes > MaxBytes && _lru.Count > 1)
        {
            var oldest = _lru.Last?.Value;
            if (oldest == null) break;
            if (string.Equals(oldest, keep, StringComparison.OrdinalIgnoreCase))
            {
                // Never evict the entry we were just asked for; stop if it's all that's left.
                if (_lru.Count <= 1) break;
                _lru.RemoveLast();
                _lru.AddFirst(oldest); // rotate so the loop can reach the rest
                continue;
            }

            Remove(oldest, ref toDispose);
        }

        // Dispose after the loop but still under the lock is acceptable (DecodedAnimation.Dispose
        // takes the Skia gate, not _gate), and keeps eviction simple.
        DisposeAll(toDispose);
    }

    private void Remove(string key, ref List<DecodedAnimation> toDispose)
    {
        if (!_entries.Remove(key, out var anim)) return;
        _lru.Remove(key);
        _bytes -= anim.TotalBytes;
        (toDispose ??= new List<DecodedAnimation>()).Add(anim);
    }

    private static void DisposeAll(List<DecodedAnimation> toDispose)
    {
        if (toDispose == null) return;
        foreach (var anim in toDispose)
            anim.Dispose();
    }
}
