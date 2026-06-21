namespace LoupixDeck.Services.Animation;

/// <summary>
/// Decodes animated image assets once and caches the resulting <see cref="DecodedAnimation"/> keyed
/// by asset relative path, so the same animation used on several buttons (or across devices) decodes
/// a single time. A root singleton — decoding is device-agnostic. Memory is bounded by an LRU budget;
/// <see cref="Trim"/> lets a caller drop animations no longer referenced by the active page.
/// </summary>
public interface IAnimatedImageCache
{
    /// <summary>
    /// Returns the decoded animation for <paramref name="relativePath"/>, decoding (and caching) it
    /// on first request. Returns null when the asset is missing or undecodable.
    /// </summary>
    DecodedAnimation Get(string relativePath);

    /// <summary>
    /// Disposes and drops every cached animation whose key is not in
    /// <paramref name="referencedRelativePaths"/>. Called on page change so animations only the
    /// previous page used release their pixel memory.
    /// </summary>
    void Trim(IEnumerable<string> referencedRelativePaths);

    /// <summary>Disposes and drops everything.</summary>
    void Clear();
}
