using Avalonia.Media.Imaging;

namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Produces the icon for a discovered application, in the two shapes the app needs: a decoded
/// bitmap for the picker, and a file on disk for the moment an icon is committed to a button.
/// </summary>
public interface IAppIconExtractor
{
    /// <summary>
    /// A bitmap scaled to <paramref name="width"/> for display in a list. Cached in memory and
    /// shared between callers; the returned bitmap is owned by the extractor, so callers must not
    /// dispose it. Null when the application has no usable icon.
    /// </summary>
    Task<Bitmap> GetThumbnailAsync(InstalledApp app, int width, CancellationToken cancellationToken = default);

    /// <summary>
    /// Path to an image file for <paramref name="app"/>, extracting it first if necessary. Intended
    /// for handing to <c>IAssetService.Import</c> when the icon is put on a button, which copies it
    /// into the content-addressed asset store. Null when there is no usable icon.
    /// </summary>
    Task<string> GetIconFileAsync(InstalledApp app, CancellationToken cancellationToken = default);
}

/// <summary>Fallback for platforms with no icon backend.</summary>
public sealed class NoOpAppIconExtractor : IAppIconExtractor
{
    public Task<Bitmap> GetThumbnailAsync(InstalledApp app, int width, CancellationToken cancellationToken = default)
        => Task.FromResult<Bitmap>(null);

    public Task<string> GetIconFileAsync(InstalledApp app, CancellationToken cancellationToken = default)
        => Task.FromResult<string>(null);
}